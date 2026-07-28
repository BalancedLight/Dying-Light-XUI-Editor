using System.Numerics;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

internal static class IncrementalTimelineFrameEvaluator
{
    private static readonly HashSet<XuiTimelineProperty> SupportedProperties =
    [
        XuiTimelineProperty.Show,
        XuiTimelineProperty.Opacity,
        XuiTimelineProperty.Position,
        XuiTimelineProperty.Scale,
        XuiTimelineProperty.Rotation,
        XuiTimelineProperty.Pivot,
        XuiTimelineProperty.Color,
        XuiTimelineProperty.TextColor,
        XuiTimelineProperty.DefaultFontColor,
    ];

    public static bool TrySample(
        XuiRenderFrame previousFrame,
        XuiTimelineEvaluationState previousState,
        XuiTimelineEvaluationState timelineState,
        XuiTimelineScopeCatalog catalog,
        TimelineAnimationCache animationCache,
        DyingLightLayoutCompilation compilation,
        XuiRenderContext renderContext,
        out XuiRenderSample sample)
    {
        sample = null!;
        if (previousState.Mode != timelineState.Mode ||
            previousState.DefaultTick != timelineState.DefaultTick)
        {
            return false;
        }

        XuiTimelineScope? changedScope = null;
        int previousTick = 0;
        int currentTick = 0;
        foreach (XuiTimelineScope scope in catalog.Scopes)
        {
            int oldTick = previousState.TickFor(scope.ScopeKey);
            int newTick = timelineState.TickFor(scope.ScopeKey);
            if (oldTick == newTick)
            {
                continue;
            }

            if (changedScope is not null)
            {
                return false;
            }

            changedScope = scope;
            previousTick = oldTick;
            currentTick = newTick;
        }

        if (changedScope is null)
        {
            sample = new XuiRenderSample(
                previousFrame,
                [],
                FullEvaluationRequired: false);
            return true;
        }

        Dictionary<
            (string Target, XuiTimelineProperty Property),
            XuiAnimatedValue> oldValues = [];
        Dictionary<
            (string Target, XuiTimelineProperty Property),
            XuiAnimatedValue> newValues = [];
        foreach (XuiTimeline timeline in changedScope.Timelines)
        {
            foreach (XuiTrack track in timeline.Tracks)
            {
                XuiAnimatedValue? oldValue =
                    TimelineEvaluator.Sample(track, previousTick);
                XuiAnimatedValue? newValue =
                    TimelineEvaluator.Sample(track, currentTick);
                if (oldValue is not null)
                {
                    oldValues[(timeline.TargetId, track.Property)] = oldValue;
                }

                if (newValue is not null)
                {
                    newValues[(timeline.TargetId, track.Property)] = newValue;
                }
            }
        }

        Dictionary<string, HashSet<XuiTimelineProperty>> changedByTarget =
            new(StringComparer.Ordinal);
        foreach (((string target, XuiTimelineProperty property), XuiAnimatedValue value)
                 in newValues)
        {
            if (oldValues.TryGetValue(
                    (target, property),
                    out XuiAnimatedValue? oldValue) &&
                Equals(oldValue, value))
            {
                continue;
            }

            if (!SupportedProperties.Contains(property))
            {
                return false;
            }

            if (!changedByTarget.TryGetValue(
                    target,
                    out HashSet<XuiTimelineProperty>? properties))
            {
                properties = [];
                changedByTarget[target] = properties;
            }

            properties.Add(property);
        }

        if (oldValues.Keys.Any(key => !newValues.ContainsKey(key)))
        {
            return false;
        }

        if (changedByTarget.Count == 0)
        {
            sample = new XuiRenderSample(
                previousFrame,
                [],
                FullEvaluationRequired: false);
            return true;
        }

        List<XuiRenderNode> nodes = previousFrame.Nodes.ToList();
        Dictionary<string, int> indexByKey = nodes
            .Select(static (node, index) => (node.Key, Index: index))
            .ToDictionary(
                static entry => entry.Key,
                static entry => entry.Index,
                StringComparer.Ordinal);
        Dictionary<string, List<int>> childrenByParent =
            nodes
                .Select(static (node, index) => (Node: node, Index: index))
                .Where(static entry => entry.Node.ParentKey is not null)
                .GroupBy(
                    static entry => entry.Node.ParentKey!,
                    StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Select(entry => entry.Index).ToList(),
                    StringComparer.Ordinal);
        HashSet<string> propagationRoots = new(StringComparer.Ordinal);
        HashSet<int> directlyChanged = [];

        foreach ((string target, HashSet<XuiTimelineProperty> properties)
                 in changedByTarget)
        {
            foreach (int index in MatchingNodes(
                         nodes,
                         changedScope,
                         target))
            {
                XuiRenderNode node = nodes[index];
                IReadOnlyDictionary<string, string>? runtime =
                    renderContext.PropertiesFor(
                        node.Id,
                        node.SelectionKey);
                IReadOnlyDictionary<string, XuiAnimatedValue>? overrides =
                    animationCache.ForNode(
                        timelineState,
                        node.Id,
                        node.SelectionKey,
                        compilation.TimelineRecursionBarrier(
                            node.SelectionKey));
                foreach (XuiTimelineProperty property in properties)
                {
                    string propertyName = property.ToString();
                    if (runtime?.ContainsKey(propertyName) == true ||
                        (property is XuiTimelineProperty.Show or
                             XuiTimelineProperty.Opacity &&
                         renderContext.IsForceShown(
                             node.Id,
                             node.SelectionKey)) ||
                        (property == XuiTimelineProperty.Show &&
                         renderContext.IsForceHidden(
                             node.Id,
                             node.SelectionKey)))
                    {
                        continue;
                    }

                    if (overrides is null ||
                        !overrides.TryGetValue(
                            propertyName,
                            out XuiAnimatedValue? value) ||
                        !TryApply(
                            node,
                            property,
                            value,
                            overrides,
                            compilation,
                            out XuiRenderNode updated,
                            out bool propagates))
                    {
                        return false;
                    }

                    node = updated;
                    if (propagates)
                    {
                        propagationRoots.Add(node.Key);
                    }
                }

                if (!Equals(nodes[index], node))
                {
                    nodes[index] = node;
                    directlyChanged.Add(index);
                }
            }
        }

        HashSet<string> minimalRoots = new(
            propagationRoots,
            StringComparer.Ordinal);
        foreach (string key in propagationRoots)
        {
            string? parentKey = nodes[indexByKey[key]].ParentKey;
            while (parentKey is not null)
            {
                if (propagationRoots.Contains(parentKey))
                {
                    minimalRoots.Remove(key);
                    break;
                }

                parentKey = indexByKey.TryGetValue(
                    parentKey,
                    out int parentIndex)
                    ? nodes[parentIndex].ParentKey
                    : null;
            }
        }

        HashSet<int> affected = new(directlyChanged);
        foreach (string rootKey in minimalRoots)
        {
            RecomputeSubtree(indexByKey[rootKey]);
        }

        string[] changedKeys = affected
            .Where(index => !Equals(previousFrame.Nodes[index], nodes[index]))
            .Order()
            .Select(index => nodes[index].Key)
            .ToArray();
        XuiRenderFrame frame = previousFrame with
        {
            Nodes = nodes,
        };
        sample = new XuiRenderSample(
            frame,
            changedKeys,
            FullEvaluationRequired: false);
        return true;

        void RecomputeSubtree(int index)
        {
            XuiRenderNode node = nodes[index];
            XuiRenderNode? parent =
                node.ParentKey is string parentKey &&
                indexByKey.TryGetValue(parentKey, out int parentIndex)
                    ? nodes[parentIndex]
                    : null;
            Matrix3x2 localTransform =
                DyingLightLayoutEngine.CreateLocalTransform(
                    node.Position,
                    node.Pivot,
                    node.Scale,
                    node.RotationDegrees);
            Matrix3x2 worldTransform = parent is null
                ? localTransform
                : localTransform * parent.WorldTransform;
            XuiRect worldBounds =
                DyingLightLayoutEngine.TransformBounds(
                    node.LocalBounds,
                    worldTransform);
            XuiRect? clipBounds = parent?.ClipBounds;
            if (node.EstablishesClip)
            {
                clipBounds = DyingLightLayoutEngine.Intersect(
                    clipBounds,
                    worldBounds);
            }

            XuiRenderNode updated = node with
            {
                Opacity = node.ForceShown
                    ? 1
                    : node.LocalOpacity * (parent?.Opacity ?? 1),
                IsShown = node.ForceShown ||
                          (node.LocalIsShown &&
                           (parent?.IsShown ?? true)),
                LocalTransform = localTransform,
                WorldTransform = worldTransform,
                WorldBounds = worldBounds,
                ClipBounds = clipBounds,
            };
            nodes[index] = updated;
            affected.Add(index);
            if (!childrenByParent.TryGetValue(
                    node.Key,
                    out List<int>? children))
            {
                return;
            }

            foreach (int child in children)
            {
                RecomputeSubtree(child);
            }
        }
    }

    private static IEnumerable<int> MatchingNodes(
        List<XuiRenderNode> nodes,
        XuiTimelineScope scope,
        string target)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            XuiRenderNode node = nodes[index];
            if (!node.IsVisualTemplatePart &&
                node.Id.Equals(target, StringComparison.Ordinal) &&
                XuiTimelineScopeCatalog.IsAncestorOrSelf(
                    scope.ScopeKey,
                    node.SelectionKey))
            {
                yield return index;
            }
        }
    }

    private static bool TryApply(
        XuiRenderNode node,
        XuiTimelineProperty property,
        XuiAnimatedValue value,
        IReadOnlyDictionary<string, XuiAnimatedValue> overrides,
        DyingLightLayoutCompilation compilation,
        out XuiRenderNode updated,
        out bool propagates)
    {
        updated = node;
        propagates = false;
        switch (property)
        {
            case XuiTimelineProperty.Show
                when value.Kind == XuiTimelineValueKind.Boolean:
                updated = node with { LocalIsShown = value.Boolean };
                propagates = true;
                return true;
            case XuiTimelineProperty.Opacity
                when value.Kind == XuiTimelineValueKind.Number:
                updated = node with
                {
                    LocalOpacity = Math.Clamp(value.Number, 0, 1),
                };
                propagates = true;
                return true;
            case XuiTimelineProperty.Position:
                if (!TryVector3(value, node.Position.Z, out XuiVector3 position))
                {
                    return false;
                }

                updated = node with { Position = position };
                propagates = true;
                return true;
            case XuiTimelineProperty.Scale:
                if (!TryVector3(value, node.Scale.Z, out XuiVector3 scale))
                {
                    return false;
                }

                if (scale.X == 0 && scale.Y == 0)
                {
                    scale = new XuiVector3(1, 1, scale.Z);
                }

                updated = node with { Scale = scale };
                propagates = true;
                return true;
            case XuiTimelineProperty.Pivot:
                if (!TryVector3(value, node.Pivot.Z, out XuiVector3 pivot))
                {
                    return false;
                }

                updated = node with { Pivot = pivot };
                propagates = true;
                return true;
            case XuiTimelineProperty.Rotation:
                double rotation = value.Kind switch
                {
                    XuiTimelineValueKind.Number => value.Number,
                    XuiTimelineValueKind.Vector3 => value.Vector3.Z,
                    XuiTimelineValueKind.Quaternion =>
                        value.Quaternion.ZRotationDegrees,
                    _ => double.NaN,
                };
                if (!double.IsFinite(rotation))
                {
                    return false;
                }

                updated = node with { RotationDegrees = rotation };
                propagates = true;
                return true;
            case XuiTimelineProperty.Color:
                if (node.Kind != XuiRenderKind.Text &&
                    value.Kind == XuiTimelineValueKind.Color)
                {
                    updated = node with { Color = value.Color };
                }

                return true;
            case XuiTimelineProperty.TextColor:
                if (node.Kind == XuiRenderKind.Text &&
                    value.Kind == XuiTimelineValueKind.Color)
                {
                    updated = node with { Color = value.Color };
                }

                return true;
            case XuiTimelineProperty.DefaultFontColor:
                if (node.Kind == XuiRenderKind.Text &&
                    value.Kind == XuiTimelineValueKind.Color &&
                    !compilation.HasAuthoredProperty(
                        node.SelectionKey,
                        "TextColor") &&
                    !overrides.ContainsKey("TextColor"))
                {
                    updated = node with { Color = value.Color };
                }

                return true;
            default:
                return false;
        }
    }

    private static bool TryVector3(
        XuiAnimatedValue value,
        double fallbackZ,
        out XuiVector3 result)
    {
        if (value.Kind == XuiTimelineValueKind.Vector3)
        {
            result = value.Vector3;
            return true;
        }

        if (value.Kind == XuiTimelineValueKind.Vector2)
        {
            result = new XuiVector3(
                value.Vector2.X,
                value.Vector2.Y,
                fallbackZ);
            return true;
        }

        result = default;
        return false;
    }
}
