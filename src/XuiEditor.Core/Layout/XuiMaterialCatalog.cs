namespace XuiEditor.Core.Layout;

public enum XuiMaterialBehavior
{
    DefaultAlpha,
    Text,
    Clip,
    Tint,
    GroupPassThrough,
    RuntimeGenerated,
    Unsupported,
}

public sealed record XuiMaterialProfile(
    string Name,
    XuiMaterialBehavior Behavior,
    bool IsExact,
    bool SuppressSelfPaint,
    string Description)
{
    public bool IsApproximation => !IsExact;

    public bool RequiresRuntimeData =>
        Behavior == XuiMaterialBehavior.RuntimeGenerated;

    public static XuiMaterialProfile Default { get; } = new(
        string.Empty,
        XuiMaterialBehavior.DefaultAlpha,
        IsExact: true,
        SuppressSelfPaint: false,
        "Default alpha-blended XUI paint.");
}

public static class XuiMaterialCatalog
{
    private static readonly HashSet<string> DefaultAlphaMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "sprite.mat",
        "menu_sprite.mat",
        "menu_antialias.mat",
        "menu_button_back.mat",
        "menu_button_back100.mat",
        "menu_ingame_background.mat",
        "menuingame_back.mat",
        "loading_bckg.mat",
        "logo_sprite.mat",
        "sprite_design.mat",
        "sprite_margin.mat",
        "sprite_margin_black.mat",
    };

    private static readonly HashSet<string> TextMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "menu_text.mat",
        "sprite_text_vc.mat",
        "sprite_text_vc_white.mat",
        "menu_loading_text.mat",
    };

    private static readonly HashSet<string> ClipMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "menu_clip.mat",
        "menu_mask_clip.mat",
        "menu_sprite_clip.mat",
        "menu_text_clip.mat",
        "menu_antialias_clip.mat",
        "menu_antialias_wrap_clip.mat",
        "sprite_text_vc_clip.mat",
        "hud_car_fuel_bar.mat",
        "hud_car_weapon_bar.mat",
        "hud_flashback_mask.mat",
    };

    private static readonly HashSet<string> TintMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "hud_colorize.mat",
        "hud_item_info.mat",
        "menu_col_mul.mat",
        "sprite_multiplyblend.mat",
        "sprite_blend_mul_alpha.mat",
        "sprite_additiveblend.mat",
        "sprite_Rotate.mat",
        "menu_antialias_orange_grad.mat",
        "menu_slider_mask_nmul.mat",
        "menu_treshold.mat",
        "menu_treshold_inverted.mat",
        "menu_gamma.mat",
        "menu_bckg_screenshot_alpha.mat",
    };

    private static readonly HashSet<string> GroupMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "button_main_group.mat",
    };

    private static readonly string[] RuntimeMaterialTokens =
    [
        "map_",
        "radar",
        "fog_of_war",
        "menu_circle_piece",
        "hud_noise",
        "pvp_edge",
        "pvp_damage_indicator",
    ];

    private static readonly HashSet<string> RuntimeTransparentMaterials = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "menu_viewport.mat",
        "pie.mat",
    };

    public static XuiMaterialProfile Resolve(
        string? material,
        XuiRenderKind kind)
    {
        string normalized = material?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return XuiMaterialProfile.Default;
        }

        if (GroupMaterials.Contains(normalized) ||
            kind == XuiRenderKind.Group &&
            normalized.Contains(
                "group",
                StringComparison.OrdinalIgnoreCase))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.GroupPassThrough,
                IsExact: true,
                SuppressSelfPaint: true,
                "The material affects descendants and does not paint the group itself.");
        }

        if (ClipMaterials.Contains(normalized) ||
            normalized.Contains(
                "clip",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(
                "_clip.mat",
                StringComparison.OrdinalIgnoreCase))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.Clip,
                IsExact: false,
                SuppressSelfPaint: false,
                "The editor preserves transformed clipping, but proprietary alpha-mask sampling is approximated.");
        }

        if (TextMaterials.Contains(normalized) ||
            normalized.Contains(
                "text",
                StringComparison.OrdinalIgnoreCase))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.Text,
                IsExact: true,
                SuppressSelfPaint: false,
                "Static vertex-colored text paint.");
        }

        if (DefaultAlphaMaterials.Contains(normalized) ||
            normalized.StartsWith(
                "menu_antialias",
                StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains(
                "grad",
                StringComparison.OrdinalIgnoreCase))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.DefaultAlpha,
                IsExact: true,
                SuppressSelfPaint: false,
                "Static alpha-blended texture or color paint.");
        }

        if (TintMaterials.Contains(normalized) ||
            normalized.Contains(
                "colorize",
                StringComparison.OrdinalIgnoreCase))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.Tint,
                IsExact: false,
                SuppressSelfPaint: false,
                "The editor applies authored color modulation without emulating the proprietary shader.");
        }

        if (RuntimeTransparentMaterials.Contains(normalized))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.RuntimeGenerated,
                IsExact: false,
                SuppressSelfPaint: true,
                "The material is generated from a runtime viewport or procedural shape and has no truthful static fill.");
        }

        if (RuntimeMaterialTokens.Any(token =>
                normalized.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return new XuiMaterialProfile(
                normalized,
                XuiMaterialBehavior.RuntimeGenerated,
                IsExact: false,
                SuppressSelfPaint: kind is XuiRenderKind.Group or
                    XuiRenderKind.Shape,
                "The material depends on runtime-generated geometry, textures, or shader constants.");
        }

        return new XuiMaterialProfile(
            normalized,
            XuiMaterialBehavior.Unsupported,
            IsExact: false,
            SuppressSelfPaint: false,
            "The proprietary material is rendered with the nearest static alpha/tint behavior.");
    }
}
