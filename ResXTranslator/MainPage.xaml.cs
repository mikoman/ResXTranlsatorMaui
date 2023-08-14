using DeepL;

namespace ResXTranslator;

public partial class MainPage : ContentPage
{
    private string _selectedResXFilePath;

    public string SelectedLanguage { get; set; }
    string authKey = "29ea5c64-9567-b0e1-def2-f9801f1b1909:fx";
    public MainPage()
    {
        InitializeComponent();
        PopulateLanguages();
    }

    public Dictionary<string, string> ReadResXFile(string path)
    {
        var values = new Dictionary<string, string>();

        var reader = new ResXParser();
        var entries = reader.ReadResXFile(path);

        foreach (var entry in entries)
        {
            values[entry.Key.ToString()] = entry.Value.ToString();
        }

        return values;
    }



    public async Task<string> TranslateTextAsync(string text, string targetLanguage)
    {

        var translator = new DeepL.Translator(authKey);
        var usage = await translator.GetUsageAsync();
        var translatedText = await translator.TranslateTextAsync(
          text,
          LanguageCode.English,
         targetLanguage);

        StatusLabel.Text = "Translated: " + translatedText.Text + "usage of character is:" + usage.Character;
        return translatedText.Text;

    }


    public void WriteResXFile(string path, Dictionary<string, string> values)
    {
        var writer = new ResXParser();
        writer.WriteResXFile(path, values);
    }

    public void PopulateLanguages()
    {
        var languages = new List<string>
        {
            "pt",
            "it",
            "de",
            "es",
            "fr"
        };

        LanguagePicker.ItemsSource = languages;
    }

    private async void OnFilePickerButtonClicked(object sender, EventArgs e)
    {
        try
        {
            FileResult result = await FilePicker.PickAsync(new PickOptions
            {

                PickerTitle = "Select a ResX File"
            });

            if (result != null)
            {
                _selectedResXFilePath = result.FullPath;
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions
        }
    }


    private async void OnTranslateButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var path = _selectedResXFilePath ?? throw new Exception("Please select a ResX file first.");
            var values = ReadResXFile(path);

            // If a specific language isn't selected, translate for all languages.
            // Otherwise, translate only for the selected language.
            List<string> languagesToTranslate = LanguagePicker.SelectedItem == null
                ? LanguagePicker.Items.Cast<string>().ToList()
                : new List<string> { LanguagePicker.SelectedItem.ToString() };

            foreach (var targetLanguage in languagesToTranslate)
            {
                var translatedValues = new Dictionary<string, string>();

                int totalEntries = values.Count;
                int processedEntries = 0;
                foreach (var entry in values)
                {
                    translatedValues[entry.Key] = await TranslateTextAsync(entry.Value, targetLanguage);
                    processedEntries++;

                    // Calculate the progress as a fraction
                    double progress = (double)processedEntries / totalEntries;

                    // Update the ProgressBar
                    await translationProgressBar.ProgressTo(progress, 50, Easing.Linear);  // 50ms for a smooth transition
                }

                string outputPath = $"/Users/miko/Projects/ResXTranslator/ResXTranslator/bin/Debug/net7.0-maccatalyst/maccatalyst-x64/AppResources{targetLanguage}.resx";
                WriteResXFile(outputPath, translatedValues);
            }

            StatusLabel.Text = "Translation complete!";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"An error occurred: {ex.Message}";
        }
    }

    private void SaveToExcelButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var path = _selectedResXFilePath ?? throw new Exception("Please select a ResX file first.");
            var values = ReadResXFile(path);

            var excelGenerator = new ExcelGenerator();
            string excelPath = $"/Users/miko/Projects/ResXTranslator/ResXTranslator/bin/Debug/net7.0-maccatalyst/maccatalyst-x64/enGB.xlsx"; // Change this to the desired path
            excelGenerator.WriteResXToExcel(excelPath, values);

            StatusLabel.Text = "Data exported to Excel successfully!";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"An error occurred: {ex.Message}";
        }
    }

}


