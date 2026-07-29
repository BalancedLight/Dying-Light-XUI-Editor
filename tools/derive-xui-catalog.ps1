param(
    [Parameter(Mandatory = $true)]
    [string] $DyingLightData0,

    [Parameter(Mandatory = $true)]
    [string] $Chrome6ReferenceRoot,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Xml.Linq

$propertyValues = @{}
$tagProperties = @{}
$overrideProperties = @{}
$overrideTags = @{}
$timelineProperties = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$stockProperties = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$stockTags = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$stockXuiCount = 0

function Add-SetValue {
    param(
        [hashtable] $Table,
        [string] $Key,
        [string] $Value
    )

    if (-not $Table.ContainsKey($Key)) {
        $Table[$Key] = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal)
    }

    [void] $Table[$Key].Add($Value)
}

function Add-ListValue {
    param(
        [hashtable] $Table,
        [string] $Key,
        [string] $Value
    )

    if (-not $Table.ContainsKey($Key)) {
        $Table[$Key] = [System.Collections.Generic.List[string]]::new()
    }

    $Table[$Key].Add($Value)
}

function Is-StructuralElement {
    param([string] $Name)

    return $Name -in @(
        "Properties",
        "Timelines",
        "Timeline",
        "TimelineProp",
        "KeyFrame",
        "Prop",
        "NamedFrames",
        "NamedFrame",
        "Id",
        "Time",
        "Interpolation",
        "EaseIn",
        "EaseOut",
        "EaseScale",
        "Name",
        "Command",
        "CommandParams")
}

$archive = [System.IO.Compression.ZipFile]::OpenRead(
    [System.IO.Path]::GetFullPath($DyingLightData0))
try {
    foreach ($entry in $archive.Entries |
        Where-Object { $_.FullName.EndsWith(".xui", [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName) {
        $stockXuiCount++
        $stream = $entry.Open()
        try {
            $document = [System.Xml.Linq.XDocument]::Load(
                $stream,
                [System.Xml.Linq.LoadOptions]::PreserveWhitespace)
        }
        finally {
            $stream.Dispose()
        }

        foreach ($element in $document.Descendants()) {
            $name = $element.Name.LocalName
            if ($name -eq "TimelineProp") {
                [void] $timelineProperties.Add($element.Value.Trim())
            }

            if ($null -eq $element.Parent -or
                $element.Parent.Name.LocalName -ne "Properties") {
                continue
            }

            [void] $stockProperties.Add($name)
            Add-ListValue $propertyValues $name $element.Value.Trim()
        }

        foreach ($element in $document.Descendants()) {
            $tag = $element.Name.LocalName
            if (Is-StructuralElement $tag) {
                continue
            }

            $propertiesNode = $element.Elements() |
                Where-Object { $_.Name.LocalName -eq "Properties" } |
                Select-Object -First 1
            if ($null -eq $propertiesNode) {
                continue
            }

            [void] $stockTags.Add($tag)
            foreach ($propertyNode in $propertiesNode.Elements()) {
                Add-SetValue $tagProperties $tag $propertyNode.Name.LocalName
            }

            $classOverride = $propertiesNode.Elements() |
                Where-Object { $_.Name.LocalName -eq "ClassOverride" } |
                Select-Object -First 1
            if ($null -eq $classOverride -or
                [string]::IsNullOrWhiteSpace($classOverride.Value)) {
                continue
            }

            $override = $classOverride.Value.Trim()
            Add-ListValue $overrideTags $override $tag
            foreach ($propertyNode in $propertiesNode.Elements()) {
                Add-SetValue $overrideProperties $override $propertyNode.Name.LocalName
            }
        }
    }
}
finally {
    $archive.Dispose()
}

$referenceDefinitions = @{}
$referenceClasses = @{}
$extensionFiles = @(
    "editorextension.xml",
    "menueditorextension.xml",
    "hudeditorextension.xml")
foreach ($extensionFile in $extensionFiles) {
    $path = Join-Path $Chrome6ReferenceRoot $extensionFile
    [xml] $extension = Get-Content -LiteralPath $path -Raw
    foreach ($class in $extension.XUIClassExtension.XUIClass) {
        $className = [string] $class.Name
        $directProperties = [System.Collections.Generic.List[string]]::new()
        foreach ($property in $class.PropDef) {
            $propertyName = [string] $property.Name
            $directProperties.Add($propertyName)
            if (-not $referenceDefinitions.ContainsKey($propertyName)) {
                $referenceDefinitions[$propertyName] =
                    [System.Collections.Generic.List[object]]::new()
            }

            $referenceDefinitions[$propertyName].Add([ordered]@{
                type = [string] $property.Type
                flags = [string] $property.Flags
                defaultValue = [string] $property.DefaultVal
            })
        }

        $referenceClasses[$className] = [ordered]@{
            name = $className
            baseClassName = [string] $class.BaseClassName
            defaultWidth = [double]::Parse(
                [string] $class.DefaultWidth,
                [System.Globalization.CultureInfo]::InvariantCulture)
            defaultHeight = [double]::Parse(
                [string] $class.DefaultHeight,
                [System.Globalization.CultureInfo]::InvariantCulture)
            description = [string] $class.Description
            directProperties = @($directProperties | Sort-Object -Unique)
        }
    }
}

$booleanNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "AButtonPress", "AdjustToLeft", "AdjustToRight", "AlignToRight",
        "AllowAlternateTextDataAssociation", "AllowJumpsFromParents",
        "AutoAdjustHeight", "AutoAdjustWidth", "AutoScrollBar",
        "AutoSizeParentToText", "AutoSizeParentX", "AutoSizeParentY",
        "AutoSizeToContentX", "AutoSizeToContentY", "AutoSizeToText",
        "BackgroundVisible", "Bold", "CanFocus", "CenterWhileIdle",
        "ClipChildren",
        "ColorControlSequenceEnabled", "DefaultFocus", "DesignTime",
        "DisableFocusRecursion", "DisableTimelineRecursion", "Enabled",
        "EnableEffects", "ExecuteOwnNamedFrames", "FocusSetSelection",
        "ForceMaterials", "HoldAspectPivotPosition", "HoldAspectRatio",
        "HoldAspectRatioX", "Horizontal", "InverseOrder", "Italic",
        "ItemsCanFocus", "JapaneseWordWrap", "KeepHeight",
        "KeepHeightOnParentSizeChange",
        "KeepHeightOnResolutionChange", "KeepPosX",
        "KeepPosXOnParentSizeChange", "KeepPosXOnResolutionChange",
        "KeepPosY", "KeepPosYOnParentSizeChange",
        "KeepPosYOnResolutionChange", "KeepWidth",
        "KeepWidthOnParentSizeChange", "KeepWidthOnResolutionChange",
        "LooseSelectionOnFocusClear", "MouseSetsFocus", "MoveSelection",
        "MoveTopOnFocus", "MultiLine", "MultilineAutoSizeHeight",
        "OverrideNoMaskMaterial", "Play", "ProlongueJumpPropagation",
        "RandomizeOnFocus", "RandomizeOnSelect", "ResizeByContentHeight",
        "ResizeByContentWidth", "RotateImageL90", "RoundPosition",
        "RoundPositionX", "RoundPositionY", "ScaleWidthByResolution",
        "SelectionSetsFocus", "Shadow", "Show", "SkipInvisible",
        "SkipInvisibleWhenAutoSize", "Strike", "TextureWrapY", "Underline",
        "UnfocusedInput", "Uppercase", "UseEffect", "UseLeftMargins",
        "UseMask", "UseOnlySizeWhenAutoSizing", "UseOpacityChange",
        "UseOpacityForArrangeItems", "UseOpacityWhenAutoSize",
        "UseScreenTransform", "UseVertexColor", "Vertical",
        "VerticalAlignDown", "VisibleOnPC", "Wrap"),
    [System.StringComparer]::Ordinal)
$integerNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "BlendMode", "BrushFlags", "Bus", "ChangePercent",
        "ClipMaskChannel", "DataAssociation", "HeightAdjust", "MaxChars",
        "NumFrames", "PressKey", "TextStyle", "WidthAdjust"),
    [System.StringComparer]::Ordinal)
$numberNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "AnimationSpeed", "BackgroundOpacity", "ChangePercent",
        "CharacterSpacingAdjust", "ContentHorizontalBorder",
        "ContentVerticalBorder", "FontYOffset", "Height", "LineSpacingAdjust",
        "MarginBottom", "MarginLeft", "MarginRight", "MarginTop",
        "NextTextureStrideX", "NextTextureStrideY", "Opacity", "Outline",
        "OutlineSize", "PointSize", "RandomizeMulFactor", "ScrollSpeed",
        "Shadow", "ShadowOffset", "SpecialSignsScale", "TextProgress", "Width"),
    [System.StringComparer]::Ordinal)
$colorNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "Color", "DefaultFontColor", "DropShadowColor", "m_CursorColor",
        "OutlineColor", "ShadowColor", "TextColor"),
    [System.StringComparer]::Ordinal)
$vector3Names = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @("Anchor", "Hud3dRotation", "Pivot", "Position", "Scale"),
    [System.StringComparer]::Ordinal)
$quaternionNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @("Const0", "Const1", "Const2", "Const3", "Rotation"),
    [System.StringComparer]::Ordinal)
$assetNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "AARectangleMaskMaterial", "BackgroundMaterial",
        "BackgroundTexture", "BaseImage", "DefaultFont", "Font",
        "FocusSound", "ImageMaskMaterial", "ImagePath", "MaskTexture",
        "Material", "PressSound", "SoundName", "TextMaskMaterial",
        "Visual"),
    [System.StringComparer]::Ordinal)
$identifierNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "Id", "AutoSizeReference", "MaskPosItem", "MaskSource",
        "MaskSource2", "MaskSource3", "NavDown", "NavLeft", "NavRight",
        "NavTabBackward", "NavTabForward", "NavUp", "PressPath"),
    [System.StringComparer]::Ordinal)

$commonProperties = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "Id", "ClassOverride", "Width", "Height", "Position", "Opacity",
        "Show", "Color", "ImagePath", "Material", "Text", "Font",
        "PointSize", "TextColor", "TextStyle", "Bold", "Italic",
        "Underline", "HorizontalAlign", "VerticalAlign", "Uppercase",
        "MultiLine", "Outline", "OutlineSize", "OutlineColor", "Shadow",
        "ShadowColor", "ShadowOffset", "ColorControlSequenceEnabled",
        "Visual", "NavUp", "NavDown",
        "NavLeft", "NavRight", "NavTabForward", "NavTabBackward"),
    [System.StringComparer]::Ordinal)
$binaryEvidence = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "Id", "Width", "Height", "Position", "Pivot", "Scale", "Rotation",
        "Opacity", "Show", "Color", "Text", "TextColor", "TextStyle",
        "Font", "ImagePath", "Material", "Bold", "Italic", "Underline",
        "Strike", "HorizontalAlign", "VerticalAlign", "SpecialSignsScale",
        "FontYOffset", "NavUp", "NavDown", "NavLeft", "NavRight",
        "NavTabForward", "NavTabBackward"),
    [System.StringComparer]::Ordinal)
$exactPreview = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "Width", "Height", "Position", "Pivot", "Scale", "Rotation",
        "Opacity", "Show", "Color", "Text", "TextColor", "TextStyle",
        "Font", "PointSize", "ImagePath", "Material", "Uppercase",
        "MultiLine", "VerticalAlignDown", "Outline", "OutlineSize",
        "OutlineColor", "Shadow", "ShadowColor", "ShadowOffset",
        "Bold", "Italic", "Underline", "HorizontalAlign", "VerticalAlign",
        "ClipChildren", "HoldAspectPivotPosition", "HoldAspectRatio",
        "HoldAspectRatioX", "KeepHeight", "KeepWidth", "KeepPosX",
        "KeepPosY", "KeepHeightOnParentSizeChange",
        "KeepWidthOnParentSizeChange", "KeepPosXOnParentSizeChange",
        "KeepPosYOnParentSizeChange", "KeepHeightOnResolutionChange",
        "KeepWidthOnResolutionChange", "KeepPosXOnResolutionChange",
        "KeepPosYOnResolutionChange", "ScaleWidthByResolution"),
    [System.StringComparer]::Ordinal)
$approximatePreview = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        "AARectangleMaskMaterial", "ForceMaterials", "ImageMaskMaterial",
        "MaskSource", "TextMaskMaterial", "UseMask", "Visual"),
    [System.StringComparer]::Ordinal)

function Get-PropertyType {
    param([string] $Name)

    if ($booleanNames.Contains($Name)) { return "Boolean" }
    if ($integerNames.Contains($Name)) { return "WholeNumber" }
    if ($numberNames.Contains($Name)) { return "Number" }
    if ($colorNames.Contains($Name)) { return "Color" }
    if ($vector3Names.Contains($Name)) { return "Vector3" }
    if ($quaternionNames.Contains($Name)) { return "Quaternion" }
    if ($assetNames.Contains($Name)) { return "AssetReference" }
    if ($identifierNames.Contains($Name)) { return "Identifier" }
    return "Textual"
}

function Get-Category {
    param([string] $Name)

    if ($Name -in @("Id", "ClassOverride", "Visual")) { return "Identity" }
    if ($Name.StartsWith("Nav", [System.StringComparison]::Ordinal)) {
        return "Navigation"
    }
    if ($Name -in @("TextProgress", "Const0", "Const1", "Const2", "Const3",
            "DisableTimelineRecursion", "AnimationSpeed")) {
        return "Animation"
    }
    if ($Name -in @("Width", "Height", "Position", "Anchor", "Pivot",
            "Scale", "Rotation") -or
        $Name.StartsWith("Keep", [System.StringComparison]::Ordinal) -or
        $Name.StartsWith("HoldAspect", [System.StringComparison]::Ordinal) -or
        $Name.IndexOf("Resolution", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name.IndexOf("ParentSize", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name.StartsWith("Margin", [System.StringComparison]::Ordinal)) {
        return "Layout"
    }
    if ($Name.IndexOf("Text", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name.IndexOf("Font", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name.IndexOf("Image", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name -in @(
            "PointSize", "Uppercase", "MultiLine", "VerticalAlignDown",
            "Outline", "OutlineSize", "OutlineColor", "Shadow",
            "ShadowColor", "DropShadowColor", "ShadowOffset", "Strike",
            "Bold", "Italic", "Underline", "HorizontalAlign",
            "VerticalAlign",
            "CharacterSpacingAdjust", "LineSpacingAdjust",
            "JapaneseWordWrap", "ColorControlSequenceEnabled")) {
        return "Text / Image"
    }
    if ($Name -in @(
            "Opacity", "Show", "Color", "Material", "UseMask",
            "MaskSource", "MaskSource2", "MaskSource3", "MaskTexture",
            "ClipChildren", "ClipMaskChannel", "ForceMaterials",
            "ImageMaskMaterial", "TextMaskMaterial",
            "AARectangleMaskMaterial", "BlendMode", "BrushFlags")) {
        return "Appearance"
    }
    if ($Name.IndexOf("Sound", [System.StringComparison]::Ordinal) -ge 0 -or
        $Name -in @("Bus", "Play")) {
        return "Sound"
    }
    return "Behavior"
}

function Get-DefaultValue {
    param(
        [string] $Name,
        [string] $Type
    )

    $known = @{
        Id = ""
        Width = "40"
        Height = "20"
        Position = "0,0,0"
        Anchor = "0,0,0"
        Pivot = "0,0,0"
        Scale = "1,1,1"
        Rotation = "0,0,0,1"
        Opacity = "1"
        Show = "true"
        Color = "0xffffffff"
        TextColor = "0xffffffff"
        DefaultFontColor = "0xffffffff"
        OutlineColor = "0xff000000"
        ShadowColor = "0xa0000000"
        PointSize = "20"
        TextStyle = "0"
        Play = "false"
    }
    if ($known.ContainsKey($Name)) {
        return $known[$Name]
    }

    if ($referenceDefinitions.ContainsKey($Name)) {
        $defaults = @($referenceDefinitions[$Name] |
            ForEach-Object { $_.defaultValue } |
            Where-Object { $null -ne $_ } |
            Sort-Object -Unique)
        if ($defaults.Count -eq 1) {
            return [string] $defaults[0]
        }
    }

    switch ($Type) {
        "Boolean" { return "false" }
        "WholeNumber" { return "0" }
        "Number" { return "0" }
        "Vector3" { return "0,0,0" }
        "Quaternion" { return "0,0,0,0" }
        "Color" { return "0xffffffff" }
        default { return "" }
    }
}

function Get-ReferenceFlags {
    param([string] $Name)

    if (-not $referenceDefinitions.ContainsKey($Name)) {
        return @()
    }

    return @($referenceDefinitions[$Name] |
        ForEach-Object { $_.flags -split "[,; ]+" } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
}

function Get-Choices {
    param(
        [string] $Name,
        [string] $Type
    )

    if ($Type -eq "Boolean") { return @("false", "true") }
    switch ($Name) {
        "ContentHorizontalAlign" { return @("left", "center", "right") }
        "DefaultHorizontalAlign" { return @("left", "center", "right") }
        "HorizontalAlign" { return @("left", "center", "right") }
        "ContentVerticalAlign" { return @("top", "middle", "bottom") }
        "DefaultVerticalAlign" { return @("top", "middle", "bottom") }
        "VerticalAlign" { return @("top", "middle", "bottom") }
        "SizeMode" { return @("0", "1", "2") }
        default { return @() }
    }
}

function Get-Description {
    param([string] $Name)

    switch ($Name) {
        "TextStyle" {
            return "Packed Chrome 6 text formatting and alignment bitmask. Unknown compatibility bits must be preserved."
        }
        "Pivot" {
            return "Unrestricted local-space XYZ origin used for scale and rotation."
        }
        "VerticalAlignDown" {
            return "Legacy Dying Light bottom-alignment property, separate from TextStyle."
        }
        "Play" {
            return "Sound timeline trigger. The editor preserves and edits it but never previews audio."
        }
        "Strike" {
            return "Standalone strike formatting exposed by Dying Light binary metadata; retained for compatibility."
        }
        { $_ -in @(
                "Bold", "Italic", "Underline", "HorizontalAlign",
                "VerticalAlign", "SpecialSignsScale", "FontYOffset") } {
            return "Standalone text property exposed by Dying Light binary metadata; it overrides the equivalent legacy TextStyle state when authored."
        }
        default {
            return "Observed in Dying Light stock XUI."
        }
    }
}

$supplementalProperties = @(
    "Bold",
    "FontYOffset",
    "HorizontalAlign",
    "Italic",
    "SpecialSignsScale",
    "Strike",
    "Underline",
    "VerticalAlign")
$catalogPropertyNames = @($stockProperties) + $supplementalProperties |
    Sort-Object -Unique
$properties = foreach ($name in $catalogPropertyNames) {
    $type = Get-PropertyType $name
    $flags = Get-ReferenceFlags $name
    [ordered]@{
        name = $name
        type = $type
        category = Get-Category $name
        defaultValue = Get-DefaultValue $name $type
        description = Get-Description $name
        choices = @(Get-Choices $name $type)
        isAdvanced = -not $commonProperties.Contains($name)
        isAnimatable = $timelineProperties.Contains($name) -or
            -not ($flags -contains "noanim")
        evidence = if ($binaryEvidence.Contains($name)) {
            "DyingLightBinary"
        }
        else {
            "DyingLightStock"
        }
        previewSupport = if ($exactPreview.Contains($name)) {
            "Exact"
        }
        elseif ($approximatePreview.Contains($name)) {
            "Approximate"
        }
        else {
            "PreserveOnly"
        }
        flags = @($flags)
    }
}

$classes = [System.Collections.Generic.List[object]]::new()
$baseClasses = @(
    [ordered]@{
        name = "XuiElement"; baseClassName = $null
        defaultWidth = 40; defaultHeight = 20
        description = "Chrome 6 UI element"
        evidence = "SharedChrome6"
        directProperties = @("Id", "Position", "Pivot", "Scale", "Rotation",
            "Opacity", "Show", "DesignTime")
    },
    [ordered]@{
        name = "XuiControl"; baseClassName = "XuiElement"
        defaultWidth = 40; defaultHeight = 20
        description = "Chrome 6 UI control"
        evidence = "SharedChrome6"
        directProperties = @("CanFocus", "Enabled", "NavUp", "NavDown",
            "NavLeft", "NavRight", "NavTabForward", "NavTabBackward")
    },
    [ordered]@{
        name = "XuiGroup"; baseClassName = "XuiElement"
        defaultWidth = 200; defaultHeight = 100
        description = "Chrome 6 UI group"
        evidence = "SharedChrome6"
        directProperties = @("ClipChildren", "Width", "Height")
    },
    [ordered]@{
        name = "XuiImage"; baseClassName = "XuiElement"
        defaultWidth = 40; defaultHeight = 20
        description = "Chrome 6 UI image"
        evidence = "SharedChrome6"
        directProperties = @("Width", "Height", "ImagePath", "Material", "Color")
    },
    [ordered]@{
        name = "XuiText"; baseClassName = "XuiElement"
        defaultWidth = 120; defaultHeight = 30
        description = "Chrome 6 UI text"
        evidence = "SharedChrome6"
        directProperties = @("Width", "Height", "Text", "Font", "PointSize",
            "TextColor", "TextStyle", "Uppercase", "MultiLine",
            "VerticalAlignDown", "Outline", "OutlineColor", "Shadow",
            "ShadowColor", "Bold", "Italic", "Underline", "Strike",
            "HorizontalAlign", "VerticalAlign", "SpecialSignsScale",
            "FontYOffset")
    },
    [ordered]@{
        name = "XuiButton"; baseClassName = "XuiControl"
        defaultWidth = 120; defaultHeight = 36
        description = "Chrome 6 UI button"
        evidence = "SharedChrome6"
        directProperties = @("Width", "Height", "Text", "Visual")
    },
    [ordered]@{
        name = "XuiScene"; baseClassName = "XuiGroup"
        defaultWidth = 1280; defaultHeight = 720
        description = "Chrome 6 UI scene"
        evidence = "SharedChrome6"
        directProperties = @()
    },
    [ordered]@{
        name = "XuiVisual"; baseClassName = "XuiGroup"
        defaultWidth = 40; defaultHeight = 20
        description = "Chrome 6 visual template"
        evidence = "SharedChrome6"
        directProperties = @()
    })
foreach ($baseClass in $baseClasses) {
    $classes.Add($baseClass)
}

function Get-InferredBaseClass {
    param([string] $Name)

    if ($Name -in @("XuiScene", "XuiVisual", "XuiCanvas")) {
        return "XuiScene"
    }
    if ($Name.IndexOf("Button", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Navi", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.EndsWith("Control", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "XuiButton"
    }
    if ($Name.IndexOf("Text", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Html", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return "XuiText"
    }
    if ($Name.IndexOf("Image", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Rectangle", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Shape", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return "XuiImage"
    }
    if ($Name.IndexOf("Group", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Panel", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Menu", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Scroll", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $Name.IndexOf("Scene", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return "XuiGroup"
    }
    return "XuiElement"
}

$knownClassNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($baseClass in $baseClasses) {
    [void] $knownClassNames.Add($baseClass.name)
}

foreach ($tag in @($stockTags | Sort-Object)) {
    if ($knownClassNames.Contains($tag)) {
        continue
    }

    $reference = $referenceClasses[$tag]
    $classes.Add([ordered]@{
        name = $tag
        baseClassName = if ($null -ne $reference) {
            $reference.baseClassName
        }
        else {
            Get-InferredBaseClass $tag
        }
        defaultWidth = if ($null -ne $reference) {
            $reference.defaultWidth
        }
        else { 40 }
        defaultHeight = if ($null -ne $reference) {
            $reference.defaultHeight
        }
        else { 20 }
        description = if ($null -ne $reference) {
            $reference.description
        }
        else { "Observed Dying Light element" }
        evidence = "DyingLightStock"
        directProperties = @($tagProperties[$tag] | Sort-Object)
    })
    [void] $knownClassNames.Add($tag)
}

foreach ($override in @($overrideProperties.Keys | Sort-Object)) {
    if ($knownClassNames.Contains($override)) {
        continue
    }

    $baseTag = $overrideTags[$override] |
        Group-Object |
        Sort-Object -Property @(
            @{ Expression = "Count"; Descending = $true },
            @{ Expression = "Name"; Descending = $false }) |
        Select-Object -First 1 -ExpandProperty Name
    $classes.Add([ordered]@{
        name = $override
        baseClassName = $baseTag
        defaultWidth = 40
        defaultHeight = 20
        description = "Observed Dying Light ClassOverride"
        evidence = "DyingLightStock"
        directProperties = @($overrideProperties[$override] | Sort-Object)
    })
    [void] $knownClassNames.Add($override)
}

foreach ($referenceClass in @($referenceClasses.Values | Sort-Object name)) {
    if ($knownClassNames.Contains($referenceClass.name)) {
        continue
    }

    $classes.Add([ordered]@{
        name = $referenceClass.name
        baseClassName = $referenceClass.baseClassName
        defaultWidth = $referenceClass.defaultWidth
        defaultHeight = $referenceClass.defaultHeight
        description = $referenceClass.description
        evidence = "Chrome6Reference"
        directProperties = @($referenceClass.directProperties |
            Where-Object { $stockProperties.Contains($_) } |
            Sort-Object)
    })
}

$catalog = [ordered]@{
    format = "dying-light-xui-catalog-v1"
    stockXuiCount = $stockXuiCount
    properties = @($properties)
    classes = @($classes | Sort-Object { $_.name })
    timelineProperties = @($timelineProperties | Sort-Object)
}

$json = $catalog | ConvertTo-Json -Depth 12
$fullOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory(
    [System.IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
[System.IO.File]::WriteAllText(
    $fullOutput,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
