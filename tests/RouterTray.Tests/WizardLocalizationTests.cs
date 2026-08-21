using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

namespace RouterTray.Tests;

public sealed class WizardLocalizationTests
{
    private static readonly string[] SupportedLocalizedCultures =
    [
        "da",
        "de",
        "es",
        "fi",
        "fr",
        "it",
        "pl",
        "pt",
        "ru",
        "sv",
        "tr",
        "uk"
    ];

    [Theory]
    [MemberData(nameof(LocalizedCultures))]
    public void WizardResources_AreCompleteAndPreserveFormatArguments(string cultureName)
    {
        var resourceManager = new ResourceManager(
            "RouterTray.Resources.Strings",
            typeof(UiText).Assembly);
        var neutralResources = Assert.IsAssignableFrom<ResourceSet>(resourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false));
        var localizedResources = Assert.IsAssignableFrom<ResourceSet>(resourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo(cultureName),
            createIfNotExists: true,
            tryParents: false));

        var wizardResources = neutralResources
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string key && key.StartsWith("Setup", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key)
            .ToArray();

        Assert.Equal(96, wizardResources.Length);
        foreach (var entry in wizardResources)
        {
            var key = Assert.IsType<string>(entry.Key);
            var neutralValue = Assert.IsType<string>(entry.Value);
            var localizedValue = localizedResources.GetString(key);

            Assert.False(
                string.IsNullOrWhiteSpace(localizedValue),
                $"Wizard resource '{key}' is missing for culture '{cultureName}'.");
            Assert.Equal(
                GetFormatArguments(neutralValue),
                GetFormatArguments(localizedValue!));
        }
    }

    public static TheoryData<string> LocalizedCultures()
    {
        var data = new TheoryData<string>();
        foreach (var culture in SupportedLocalizedCultures)
        {
            data.Add(culture);
        }

        return data;
    }

    private static string[] GetFormatArguments(string value)
    {
        return Regex.Matches(value, @"\{\d+\}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(argument => argument, StringComparer.Ordinal)
            .ToArray();
    }
}
