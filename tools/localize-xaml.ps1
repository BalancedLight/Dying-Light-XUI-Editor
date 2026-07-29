param(
    [switch] $Apply
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$wpfRoot = Join-Path $root "src\XuiEditor.Wpf"
$localizationRoot = Join-Path $wpfRoot "Localization"
$englishCatalogPath = Join-Path $localizationRoot "Strings.En.json"
$attributePattern =
    '\b(Title|Header|Content|Text|ToolTip|AutomationProperties\.Name)="([^"]*)"'
$technicalValues = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($value in @(
    "stop",
    "goto",
    "gotoandstop",
    "gotoandplay"
)) {
    [void] $technicalValues.Add($value)
}

$catalog = [ordered]@{}
$changedFiles = [System.Collections.Generic.List[string]]::new()
$xamlFiles = Get-ChildItem -LiteralPath $wpfRoot -Filter "*.xaml" |
    Where-Object { $_.Name -ne "App.xaml" } |
    Sort-Object Name
$alreadyLocalized = $xamlFiles | Where-Object {
    [System.IO.File]::ReadAllText($_.FullName).Contains(
        "{DynamicResource Ui.Xaml.",
        [System.StringComparison]::Ordinal)
} | Select-Object -First 1
if ($null -ne $alreadyLocalized) {
    if ($Apply) {
        throw (
            "The window XAML already uses Ui.Xaml resources. " +
            "Refusing to overwrite the checked-in English catalog.")
    }

    "Window XAML is already localized; no extraction is needed."
    exit 0
}

foreach ($file in $xamlFiles) {
    $state = [pscustomobject]@{
        Counter = 0
    }
    $raw = [System.IO.File]::ReadAllText($file.FullName)
    $rewritten = [System.Text.RegularExpressions.Regex]::Replace(
        $raw,
        $attributePattern,
        {
            param($match)

            $attribute = $match.Groups[1].Value
            $encodedValue = $match.Groups[2].Value
            $value = [System.Net.WebUtility]::HtmlDecode($encodedValue)
            if ($value.StartsWith(
                    "{",
                    [System.StringComparison]::Ordinal) -or
                $value -notmatch '[A-Za-z]' -or
                $technicalValues.Contains($value)) {
                return $match.Value
            }

            $state.Counter++
            $key = "Ui.Xaml.{0}.{1:D3}" -f
                $file.BaseName,
                $state.Counter
            $catalog[$key] = $value
            return '{0}="{{DynamicResource {1}}}"' -f $attribute, $key
        })

    if (-not [string]::Equals(
            $raw,
            $rewritten,
            [System.StringComparison]::Ordinal)) {
        $changedFiles.Add($file.FullName)
        if ($Apply) {
            [System.IO.File]::WriteAllText(
                $file.FullName,
                $rewritten,
                [System.Text.UTF8Encoding]::new($false))
        }
    }
}

if ($Apply) {
    [System.IO.Directory]::CreateDirectory($localizationRoot) | Out-Null
    $json = $catalog | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        $englishCatalogPath,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

"Localizable XAML entries: {0}" -f $catalog.Count
if ($Apply) {
    "Updated XAML files: {0}" -f $changedFiles.Count
    "English catalog: {0}" -f $englishCatalogPath
} else {
    "Dry run; pass -Apply to rewrite XAML and create the English catalog."
}
