using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    public class TemplateProfile : ObservableObject
    {
        private string name;
        private string template;

        public string Name { get { return name; } set { SetValue(ref name, value); } }
        public string Template { get { return template; } set { SetValue(ref template, value); } }

        public TemplateProfile()
        {
        }

        public TemplateProfile(string name, string template)
        {
            Name = name;
            Template = template;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class LanguageOption
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }

        public LanguageOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }
    }

    public class MetaDataIASettings : ObservableObject
    {
        public const string ApplySkip = "No tocar";
        public const string ApplyEmptyOnly = "Solo si esta vacio";
        public const string ApplyAppend = "Anadir sin borrar";
        public const string ApplyOverwrite = "Sobrescribir";
        public const string ProviderOpenAI = "OpenAI";
        public const string ProviderLmStudio = "LM Studio local";
        public const string ProviderOllama = "Ollama local";
        public const string ProviderGemini = "Google Gemini";
        public const string ProviderClaude = "Claude Anthropic";
        public const string ProviderOpenRouter = "OpenRouter";
        public const string ProviderGroq = "Groq";
        public const string ProviderCustom = "Personalizado compatible con OpenAI";

        private string providerPreset = ProviderOpenAI;
        private string endpoint = "https://api.openai.com/v1/chat/completions";
        private string apiKey = string.Empty;
        private string model = "gpt-4.1-mini";
        private string language = "es";
        private string descriptionTemplate = DefaultMediumTemplate;
        private ObservableCollection<TemplateProfile> templates;
        private string activeTemplateName = "Media";
        private bool enableTemplateRules = false;
        private string genreTemplateRules = "RPG=RPG\nRol=RPG\nAventura=Aventura\nIndie=Indie\nEmulacion=Emulacion\nRetro=Emulacion";
        private string platformTemplateRules = string.Empty;
        private string sourceTemplateRules = string.Empty;
        private bool overwriteExistingDescription = false;
        private bool overwriteExistingLists = false;
        private bool includeExistingMetadata = true;
        private int maxListItems = 8;
        private bool generateDescription = true;
        private bool generateGenres = true;
        private bool generateTags = true;
        private bool generateFeatures = true;
        private bool generateDevelopers = true;
        private bool generatePublishers = true;
        private bool generateAgeRatings = true;
        private bool generateRegions = true;
        private bool generateCategories = true;
        private bool generateSortingName = true;
        private bool generateLinks = false;
        private string descriptionApplyMode = ApplyEmptyOnly;
        private string genresApplyMode = ApplyAppend;
        private string tagsApplyMode = ApplyAppend;
        private string featuresApplyMode = ApplyAppend;
        private string developersApplyMode = ApplyEmptyOnly;
        private string publishersApplyMode = ApplyEmptyOnly;
        private string ageRatingsApplyMode = ApplyEmptyOnly;
        private string regionsApplyMode = ApplyEmptyOnly;
        private string categoriesApplyMode = ApplyAppend;
        private string sortingNameApplyMode = ApplyEmptyOnly;
        private string linksApplyMode = ApplyAppend;
        private int maxGenres = 4;
        private int maxTags = 10;
        private int maxFeatures = 8;
        private int maxDevelopers = 1;
        private int maxPublishers = 1;
        private int maxAgeRatings = 2;
        private int maxRegions = 3;
        private int maxCategories = 6;
        private int maxLinks = 5;
        private string tagPrefix = string.Empty;
        private string categoryPrefix = string.Empty;
        private string blacklist = string.Empty;
        private bool preferExistingGenres = false;
        private bool preferExistingTags = false;
        private bool preferExistingFeatures = false;
        private bool preferExistingCategories = false;
        private string tone = "Neutral";
        private string length = "Media";
        private string shortLength = "Media";
        private string synopsisLength = "Larga";
        private string premiseLength = "Media";
        private string gameplayLength = "Media";
        private string toneLength = "Corta";
        private string settingLength = "Media";
        private string perspectiveLength = "Corta";
        private string playModesLength = "Corta";
        private string estimatedLengthLength = "Corta";
        private string similarGamesLength = "Corta";
        private string notesLength = "Corta";
        private string recommendedForLength = "Media";
        private string extraInstructions = string.Empty;
        private string existingMetadataMode = "Usar como contexto";
        private bool strictCompanyAgeRegion = true;
        private bool enableLocalFallback = true;
        private bool tryLmStudioFallback = true;
        private bool tryOllamaFallback = true;
        private string lmStudioFallbackModel = "local-model";
        private string ollamaFallbackModel = "llama3.1";
        private bool companyLimitDefaultsMigrated = false;
        private bool autoImportNewGames = false;
        private bool autoImportGenerateMetadata = true;
        private bool autoImportGenerateMedia = false;
        private List<Guid> autoImportKnownGameIds;
        private string mediaProvider = MediaProviderSteamGridDb;
        private string steamGridDbApiKey = string.Empty;
        private bool downloadCoverImage = false;
        private bool downloadIcon = false;
        private bool downloadBackgroundImage = false;
        private string coverImageApplyMode = ApplyEmptyOnly;
        private string iconApplyMode = ApplyEmptyOnly;
        private string backgroundImageApplyMode = ApplyEmptyOnly;
        private string coverImagePreset = CoverPresetPlayniteVertical;
        private string iconPreset = IconPresetOriginal;
        private string backgroundImagePreset = BackgroundPresetSteamHero;
        private string backgroundLogoPreference = BackgroundLogoAny;
        private int mediaSearchMaxResults = 50;
        private bool mediaAvoidNsfw = true;
        private bool mediaAvoidBlurred = true;
        private bool mediaPreferOfficial = true;
        private bool mediaAvoidConsoleCovers = true;
        private bool iconSquarePreferGrid = true;
        private bool mediaUseSteamOfficial = true;
        private bool mediaUseSteamScreenshots = true;
        private bool mediaUseSteamGridDb = true;
        private bool mediaUseSteamGridDbBackgroundGrids = true;
        private bool mediaUseRawg = false;
        private string rawgApiKey = string.Empty;
        private bool mediaUseMobyGames = false;
        private string mobyGamesApiKey = string.Empty;
        private bool mediaUseIgdb = false;
        private string igdbClientId = string.Empty;
        private string igdbClientSecret = string.Empty;
        private string igdbAccessToken = string.Empty;

        public const string DefaultShortTemplate = "<p>{short}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}";
        public const string DefaultMediumTemplate = "<h3>Descripcion breve</h3>\n<p>{short}</p>\n\n<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}\n\n<h3>Modos de juego</h3>\n<p>{playModes}</p>\n\n<h3>Duracion estimada</h3>\n<p>{estimatedLength}</p>\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultLongTemplate = "<h3>Descripcion breve</h3>\n<p>{short}</p>\n\n<h3>Premisa</h3>\n<p>{premise}</p>\n\n<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Jugabilidad</h3>\n<p>{gameplay}</p>\n\n<h3>Tono y ambientacion</h3>\n<p>{tone}</p>\n<p>{setting}</p>\n\n<h3>Perspectiva y modos</h3>\n<p>{perspective}</p>\n<p>{playModes}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}\n\n<h3>Duracion estimada</h3>\n<p>{estimatedLength}</p>\n\n<h3>Juegos similares</h3>\n<p>{similarGames}</p>\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>\n\n<h3>Notas</h3>\n<p>{notes}</p>";
        public const string DefaultRpgTemplate = "<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Rol y progresion</h3>\n<p>{gameplay}</p>\n\n<h3>Mundo y tono</h3>\n<p>{setting}</p>\n<p>{tone}</p>\n\n<h3>Caracteristicas RPG</h3>\n{features}\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultAdventureTemplate = "<h3>Premisa</h3>\n<p>{premise}</p>\n\n<h3>Aventura</h3>\n<p>{synopsis}</p>\n\n<h3>Exploracion y ritmo</h3>\n<p>{gameplay}</p>\n\n<h3>Caracteristicas</h3>\n{features}\n\n<h3>Ideal para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultIndieTemplate = "<h3>Resumen</h3>\n<p>{short}</p>\n\n<h3>Propuesta</h3>\n<p>{premise}</p>\n\n<h3>Estilo</h3>\n<p>{tone}</p>\n\n<h3>Elementos clave</h3>\n{features}\n\n<h3>Para quien es</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultEmulationTemplate = "<h3>Resumen</h3>\n<p>{short}</p>\n\n<h3>Contexto</h3>\n<p>{synopsis}</p>\n\n<h3>Jugabilidad</h3>\n<p>{gameplay}</p>\n\n<h3>Datos utiles</h3>\n<p>Plataforma/perspectiva: {perspective}</p>\n<p>Modos: {playModes}</p>\n\n<h3>Caracteristicas</h3>\n{features}";
        public const string MediaProviderSteamGridDb = "SteamGridDB";
        public const string CoverPresetOriginal = "Original";
        public const string CoverPresetPlayniteVertical = "Playnite vertical (600x900)";
        public const string CoverPresetSquare = "Cuadrada Playnite (600x600)";
        public const string CoverPresetHorizontal = "Horizontal/banner (920x430)";
        public const string IconPresetOriginal = "Original/transparente";
        public const string IconPresetSquare = "Cuadrado 256";
        public const string IconPresetRounded = "Redondeado 256";
        public const string IconPresetCircle = "Redondo 256";
        public const string BackgroundPresetOriginal = "Original";
        public const string BackgroundPresetSteamHero = "Hero Steam (3840x1240)";
        public const string BackgroundPresetSteamHeroSmall = "Hero Steam ligero (1920x620)";
        public const string BackgroundPresetFullHd = "Pantalla completa Full HD (1920x1080)";
        public const string BackgroundPresetQhd = "Pantalla completa QHD (2560x1440)";
        public const string BackgroundPreset4K = "Pantalla completa 4K (3840x2160)";
        public const string BackgroundLogoAny = "Cualquiera";
        public const string BackgroundLogoPreferNoLogo = "Preferir sin logo";
        public const string BackgroundLogoPreferLogo = "Preferir con logo";

        public string ProviderPreset
        {
            get { return providerPreset; }
            set
            {
                if (string.Equals(providerPreset, value, StringComparison.Ordinal))
                {
                    return;
                }

                SetValue(ref providerPreset, value);
                OnPropertyChanged("ProviderKeyHelp");
                OnPropertyChanged("ProviderKeyUrl");
                OnPropertyChanged("ProviderBillingHelp");
            }
        }
        public string Endpoint { get { return endpoint; } set { SetValue(ref endpoint, value); } }
        public string ApiKey { get { return apiKey; } set { SetValue(ref apiKey, value); } }
        public string Model { get { return model; } set { SetValue(ref model, value); } }
        public string Language { get { return language; } set { SetValue(ref language, value); } }
        public string DescriptionTemplate { get { return descriptionTemplate; } set { SetValue(ref descriptionTemplate, value); } }
        public ObservableCollection<TemplateProfile> Templates { get { return templates; } set { SetValue(ref templates, value); } }
        public string ActiveTemplateName { get { return activeTemplateName; } set { SetValue(ref activeTemplateName, value); } }
        public bool EnableTemplateRules { get { return enableTemplateRules; } set { SetValue(ref enableTemplateRules, value); } }
        public string GenreTemplateRules { get { return genreTemplateRules; } set { SetValue(ref genreTemplateRules, value); } }
        public string PlatformTemplateRules { get { return platformTemplateRules; } set { SetValue(ref platformTemplateRules, value); } }
        public string SourceTemplateRules { get { return sourceTemplateRules; } set { SetValue(ref sourceTemplateRules, value); } }
        public bool OverwriteExistingDescription { get { return overwriteExistingDescription; } set { SetValue(ref overwriteExistingDescription, value); } }
        public bool OverwriteExistingLists { get { return overwriteExistingLists; } set { SetValue(ref overwriteExistingLists, value); } }
        public bool IncludeExistingMetadata { get { return includeExistingMetadata; } set { SetValue(ref includeExistingMetadata, value); } }
        public int MaxListItems { get { return maxListItems; } set { SetValue(ref maxListItems, Math.Max(1, Math.Min(20, value))); } }
        public bool GenerateDescription { get { return generateDescription; } set { SetValue(ref generateDescription, value); } }
        public bool GenerateGenres { get { return generateGenres; } set { SetValue(ref generateGenres, value); } }
        public bool GenerateTags { get { return generateTags; } set { SetValue(ref generateTags, value); } }
        public bool GenerateFeatures { get { return generateFeatures; } set { SetValue(ref generateFeatures, value); } }
        public bool GenerateDevelopers { get { return generateDevelopers; } set { SetValue(ref generateDevelopers, value); } }
        public bool GeneratePublishers { get { return generatePublishers; } set { SetValue(ref generatePublishers, value); } }
        public bool GenerateAgeRatings { get { return generateAgeRatings; } set { SetValue(ref generateAgeRatings, value); } }
        public bool GenerateRegions { get { return generateRegions; } set { SetValue(ref generateRegions, value); } }
        public bool GenerateCategories { get { return generateCategories; } set { SetValue(ref generateCategories, value); } }
        public bool GenerateSortingName { get { return generateSortingName; } set { SetValue(ref generateSortingName, value); } }
        public bool GenerateLinks { get { return generateLinks; } set { SetValue(ref generateLinks, value); } }
        public string DescriptionApplyMode { get { return descriptionApplyMode; } set { SetValue(ref descriptionApplyMode, value); } }
        public string GenresApplyMode { get { return genresApplyMode; } set { SetValue(ref genresApplyMode, value); } }
        public string TagsApplyMode { get { return tagsApplyMode; } set { SetValue(ref tagsApplyMode, value); } }
        public string FeaturesApplyMode { get { return featuresApplyMode; } set { SetValue(ref featuresApplyMode, value); } }
        public string DevelopersApplyMode { get { return developersApplyMode; } set { SetValue(ref developersApplyMode, value); } }
        public string PublishersApplyMode { get { return publishersApplyMode; } set { SetValue(ref publishersApplyMode, value); } }
        public string AgeRatingsApplyMode { get { return ageRatingsApplyMode; } set { SetValue(ref ageRatingsApplyMode, value); } }
        public string RegionsApplyMode { get { return regionsApplyMode; } set { SetValue(ref regionsApplyMode, value); } }
        public string CategoriesApplyMode { get { return categoriesApplyMode; } set { SetValue(ref categoriesApplyMode, value); } }
        public string SortingNameApplyMode { get { return sortingNameApplyMode; } set { SetValue(ref sortingNameApplyMode, value); } }
        public string LinksApplyMode { get { return linksApplyMode; } set { SetValue(ref linksApplyMode, value); } }
        public int MaxGenres { get { return maxGenres; } set { SetValue(ref maxGenres, Clamp(value)); } }
        public int MaxTags { get { return maxTags; } set { SetValue(ref maxTags, Clamp(value)); } }
        public int MaxFeatures { get { return maxFeatures; } set { SetValue(ref maxFeatures, Clamp(value)); } }
        public int MaxDevelopers { get { return maxDevelopers; } set { SetValue(ref maxDevelopers, Clamp(value)); } }
        public int MaxPublishers { get { return maxPublishers; } set { SetValue(ref maxPublishers, Clamp(value)); } }
        public int MaxAgeRatings { get { return maxAgeRatings; } set { SetValue(ref maxAgeRatings, Clamp(value)); } }
        public int MaxRegions { get { return maxRegions; } set { SetValue(ref maxRegions, Clamp(value)); } }
        public int MaxCategories { get { return maxCategories; } set { SetValue(ref maxCategories, Clamp(value)); } }
        public int MaxLinks { get { return maxLinks; } set { SetValue(ref maxLinks, Clamp(value)); } }
        public string TagPrefix { get { return tagPrefix; } set { SetValue(ref tagPrefix, value); } }
        public string CategoryPrefix { get { return categoryPrefix; } set { SetValue(ref categoryPrefix, value); } }
        public string Blacklist { get { return blacklist; } set { SetValue(ref blacklist, value); } }
        public bool PreferExistingGenres { get { return preferExistingGenres; } set { SetValue(ref preferExistingGenres, value); } }
        public bool PreferExistingTags { get { return preferExistingTags; } set { SetValue(ref preferExistingTags, value); } }
        public bool PreferExistingFeatures { get { return preferExistingFeatures; } set { SetValue(ref preferExistingFeatures, value); } }
        public bool PreferExistingCategories { get { return preferExistingCategories; } set { SetValue(ref preferExistingCategories, value); } }
        public string Tone { get { return tone; } set { SetValue(ref tone, value); } }
        public string Length { get { return length; } set { SetValue(ref length, value); } }
        public string ShortLength { get { return shortLength; } set { SetValue(ref shortLength, value); } }
        public string SynopsisLength { get { return synopsisLength; } set { SetValue(ref synopsisLength, value); } }
        public string PremiseLength { get { return premiseLength; } set { SetValue(ref premiseLength, value); } }
        public string GameplayLength { get { return gameplayLength; } set { SetValue(ref gameplayLength, value); } }
        public string ToneLength { get { return toneLength; } set { SetValue(ref toneLength, value); } }
        public string SettingLength { get { return settingLength; } set { SetValue(ref settingLength, value); } }
        public string PerspectiveLength { get { return perspectiveLength; } set { SetValue(ref perspectiveLength, value); } }
        public string PlayModesLength { get { return playModesLength; } set { SetValue(ref playModesLength, value); } }
        public string EstimatedLengthLength { get { return estimatedLengthLength; } set { SetValue(ref estimatedLengthLength, value); } }
        public string SimilarGamesLength { get { return similarGamesLength; } set { SetValue(ref similarGamesLength, value); } }
        public string NotesLength { get { return notesLength; } set { SetValue(ref notesLength, value); } }
        public string RecommendedForLength { get { return recommendedForLength; } set { SetValue(ref recommendedForLength, value); } }
        public string ExtraInstructions { get { return extraInstructions; } set { SetValue(ref extraInstructions, value); } }
        public string ExistingMetadataMode { get { return existingMetadataMode; } set { SetValue(ref existingMetadataMode, value); } }
        public bool StrictCompanyAgeRegion { get { return strictCompanyAgeRegion; } set { SetValue(ref strictCompanyAgeRegion, value); } }
        public bool EnableLocalFallback { get { return enableLocalFallback; } set { SetValue(ref enableLocalFallback, value); } }
        public bool TryLmStudioFallback { get { return tryLmStudioFallback; } set { SetValue(ref tryLmStudioFallback, value); } }
        public bool TryOllamaFallback { get { return tryOllamaFallback; } set { SetValue(ref tryOllamaFallback, value); } }
        public string LmStudioFallbackModel { get { return lmStudioFallbackModel; } set { SetValue(ref lmStudioFallbackModel, value); } }
        public string OllamaFallbackModel { get { return ollamaFallbackModel; } set { SetValue(ref ollamaFallbackModel, value); } }
        public bool CompanyLimitDefaultsMigrated { get { return companyLimitDefaultsMigrated; } set { SetValue(ref companyLimitDefaultsMigrated, value); } }
        public bool AutoImportNewGames { get { return autoImportNewGames; } set { SetValue(ref autoImportNewGames, value); } }
        public bool AutoImportGenerateMetadata { get { return autoImportGenerateMetadata; } set { SetValue(ref autoImportGenerateMetadata, value); } }
        public bool AutoImportGenerateMedia { get { return autoImportGenerateMedia; } set { SetValue(ref autoImportGenerateMedia, value); } }
        public List<Guid> AutoImportKnownGameIds { get { return autoImportKnownGameIds; } set { SetValue(ref autoImportKnownGameIds, value); } }
        public string MediaProvider { get { return mediaProvider; } set { SetValue(ref mediaProvider, value); } }
        public string SteamGridDbApiKey { get { return steamGridDbApiKey; } set { SetValue(ref steamGridDbApiKey, value); } }
        public bool DownloadCoverImage { get { return downloadCoverImage; } set { SetValue(ref downloadCoverImage, value); } }
        public bool DownloadIcon { get { return downloadIcon; } set { SetValue(ref downloadIcon, value); } }
        public bool DownloadBackgroundImage { get { return downloadBackgroundImage; } set { SetValue(ref downloadBackgroundImage, value); } }
        public string CoverImageApplyMode { get { return coverImageApplyMode; } set { SetValue(ref coverImageApplyMode, value); } }
        public string IconApplyMode { get { return iconApplyMode; } set { SetValue(ref iconApplyMode, value); } }
        public string BackgroundImageApplyMode { get { return backgroundImageApplyMode; } set { SetValue(ref backgroundImageApplyMode, value); } }
        public string CoverImagePreset { get { return coverImagePreset; } set { SetValue(ref coverImagePreset, value); } }
        public string IconPreset { get { return iconPreset; } set { SetValue(ref iconPreset, value); } }
        public string BackgroundImagePreset { get { return backgroundImagePreset; } set { SetValue(ref backgroundImagePreset, value); } }
        public string BackgroundLogoPreference { get { return backgroundLogoPreference; } set { SetValue(ref backgroundLogoPreference, value); } }
        public int MediaSearchMaxResults { get { return mediaSearchMaxResults; } set { SetValue(ref mediaSearchMaxResults, Math.Max(1, Math.Min(100, value))); } }
        public bool MediaAvoidNsfw { get { return mediaAvoidNsfw; } set { SetValue(ref mediaAvoidNsfw, value); } }
        public bool MediaAvoidBlurred { get { return mediaAvoidBlurred; } set { SetValue(ref mediaAvoidBlurred, value); } }
        public bool MediaPreferOfficial { get { return mediaPreferOfficial; } set { SetValue(ref mediaPreferOfficial, value); } }
        public bool MediaAvoidConsoleCovers { get { return mediaAvoidConsoleCovers; } set { SetValue(ref mediaAvoidConsoleCovers, value); } }
        public bool IconSquarePreferGrid { get { return iconSquarePreferGrid; } set { SetValue(ref iconSquarePreferGrid, value); } }
        public bool MediaUseSteamOfficial { get { return mediaUseSteamOfficial; } set { SetValue(ref mediaUseSteamOfficial, value); } }
        public bool MediaUseSteamScreenshots { get { return mediaUseSteamScreenshots; } set { SetValue(ref mediaUseSteamScreenshots, value); } }
        public bool MediaUseSteamGridDb { get { return mediaUseSteamGridDb; } set { SetValue(ref mediaUseSteamGridDb, value); } }
        public bool MediaUseSteamGridDbBackgroundGrids { get { return mediaUseSteamGridDbBackgroundGrids; } set { SetValue(ref mediaUseSteamGridDbBackgroundGrids, value); } }
        public bool MediaUseRawg { get { return mediaUseRawg; } set { SetValue(ref mediaUseRawg, value); } }
        public string RawgApiKey { get { return rawgApiKey; } set { SetValue(ref rawgApiKey, value); } }
        public bool MediaUseMobyGames { get { return mediaUseMobyGames; } set { SetValue(ref mediaUseMobyGames, value); } }
        public string MobyGamesApiKey { get { return mobyGamesApiKey; } set { SetValue(ref mobyGamesApiKey, value); } }
        public bool MediaUseIgdb { get { return mediaUseIgdb; } set { SetValue(ref mediaUseIgdb, value); } }
        public string IgdbClientId { get { return igdbClientId; } set { SetValue(ref igdbClientId, value); } }
        public string IgdbClientSecret { get { return igdbClientSecret; } set { SetValue(ref igdbClientSecret, value); } }
        public string IgdbAccessToken { get { return igdbAccessToken; } set { SetValue(ref igdbAccessToken, value); } }

        public MetaDataIASettings()
        {
            ResetTemplates();
        }

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(ProviderPreset))
            {
                ProviderPreset = ProviderOpenAI;
            }

            EnsureTextLengthDefaults();
            EnsureCompanyLimitDefaults();
            EnsureMediaDefaults();

            if (Templates == null || Templates.Count == 0)
            {
                ResetTemplates();
            }
            else
            {
                DeduplicateTemplates();
                RepairEmptyDefaultTemplates();
            }

            if (string.IsNullOrWhiteSpace(ActiveTemplateName))
            {
                ActiveTemplateName = "Media";
            }

            if (string.IsNullOrWhiteSpace(DescriptionTemplate))
            {
                var active = GetActiveTemplateWithoutEnsure();
                DescriptionTemplate = active == null || string.IsNullOrWhiteSpace(active.Template)
                    ? DefaultMediumTemplate
                    : active.Template;
            }
        }

        private void EnsureMediaDefaults()
        {
            if (AutoImportKnownGameIds == null)
            {
                AutoImportKnownGameIds = new List<Guid>();
            }

            if (string.IsNullOrWhiteSpace(MediaProvider))
            {
                MediaProvider = MediaProviderSteamGridDb;
            }

            CoverImageApplyMode = EnsureApplyMode(CoverImageApplyMode, ApplyEmptyOnly);
            IconApplyMode = EnsureApplyMode(IconApplyMode, ApplyEmptyOnly);
            BackgroundImageApplyMode = EnsureApplyMode(BackgroundImageApplyMode, ApplyEmptyOnly);
            CoverImagePreset = EnsureOption(CoverImagePreset, CoverPresetPlayniteVertical);
            IconPreset = EnsureOption(IconPreset, IconPresetOriginal);
            BackgroundImagePreset = EnsureOption(BackgroundImagePreset, BackgroundPresetSteamHero);
            BackgroundLogoPreference = EnsureOption(BackgroundLogoPreference, BackgroundLogoAny);
            if (MediaSearchMaxResults < 20)
            {
                MediaSearchMaxResults = 50;
            }
        }

        private void EnsureTextLengthDefaults()
        {
            ShortLength = EnsureLengthValue(ShortLength, "Media");
            SynopsisLength = EnsureLengthValue(SynopsisLength, "Larga");
            PremiseLength = EnsureLengthValue(PremiseLength, "Media");
            GameplayLength = EnsureLengthValue(GameplayLength, "Media");
            ToneLength = EnsureLengthValue(ToneLength, "Corta");
            SettingLength = EnsureLengthValue(SettingLength, "Media");
            PerspectiveLength = EnsureLengthValue(PerspectiveLength, "Corta");
            PlayModesLength = EnsureLengthValue(PlayModesLength, "Corta");
            EstimatedLengthLength = EnsureLengthValue(EstimatedLengthLength, "Corta");
            SimilarGamesLength = EnsureLengthValue(SimilarGamesLength, "Corta");
            NotesLength = EnsureLengthValue(NotesLength, "Corta");
            RecommendedForLength = EnsureLengthValue(RecommendedForLength, "Media");
        }

        private void EnsureCompanyLimitDefaults()
        {
            if (CompanyLimitDefaultsMigrated)
            {
                return;
            }

            if (MaxDevelopers == 3)
            {
                MaxDevelopers = 1;
            }

            if (MaxPublishers == 3)
            {
                MaxPublishers = 1;
            }

            CompanyLimitDefaultsMigrated = true;
        }

        private static string EnsureLengthValue(string value, string fallback)
        {
            return string.Equals(value, "Corta", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Media", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Larga", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Extra larga", StringComparison.OrdinalIgnoreCase)
                ? value
                : fallback;
        }

        private static string EnsureApplyMode(string value, string fallback)
        {
            return string.Equals(value, ApplySkip, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, ApplyEmptyOnly, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, ApplyAppend, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, ApplyOverwrite, StringComparison.OrdinalIgnoreCase)
                ? value
                : fallback;
        }

        private static string EnsureOption(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        public void ResetTemplates()
        {
            Templates = CreateDefaultTemplates();
            ActiveTemplateName = "Media";
            DescriptionTemplate = DefaultMediumTemplate;
        }

        public TemplateProfile GetActiveTemplate()
        {
            EnsureDefaults();
            return GetActiveTemplateWithoutEnsure();
        }

        private TemplateProfile GetActiveTemplateWithoutEnsure()
        {
            var match = Templates.FirstOrDefault(x => string.Equals(x.Name, ActiveTemplateName, StringComparison.OrdinalIgnoreCase));
            return match ?? Templates.FirstOrDefault();
        }

        public string ResolveTemplate(Game game)
        {
            EnsureDefaults();
            var templateName = string.Empty;
            if (EnableTemplateRules)
            {
                templateName = ResolveTemplateName(SourceTemplateRules, game == null || game.Source == null ? null : new List<string> { game.Source.Name });
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    templateName = ResolveTemplateName(PlatformTemplateRules, Names(game == null ? null : game.Platforms));
                }

                if (string.IsNullOrWhiteSpace(templateName))
                {
                    var typeNames = new List<string>();
                    if (game != null)
                    {
                        typeNames.AddRange(Names(game.Genres));
                        typeNames.AddRange(Names(game.Tags));
                        typeNames.AddRange(Names(game.Categories));
                    }

                    templateName = ResolveTemplateName(GenreTemplateRules, typeNames);
                }
            }

            var profile = string.IsNullOrWhiteSpace(templateName)
                ? GetActiveTemplate()
                : Templates.FirstOrDefault(x => string.Equals(x.Name, templateName, StringComparison.OrdinalIgnoreCase));

            if (profile != null && !string.IsNullOrWhiteSpace(profile.Template))
            {
                return profile.Template;
            }

            return string.IsNullOrWhiteSpace(DescriptionTemplate) ? DefaultMediumTemplate : DescriptionTemplate;
        }

        private static List<string> Names(IEnumerable<DatabaseObject> items)
        {
            if (items == null)
            {
                return new List<string>();
            }

            return items
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name)
                .ToList();
        }

        public static ObservableCollection<TemplateProfile> CreateDefaultTemplates()
        {
            return new ObservableCollection<TemplateProfile>
            {
                new TemplateProfile("Corta", DefaultShortTemplate),
                new TemplateProfile("Media", DefaultMediumTemplate),
                new TemplateProfile("Larga", DefaultLongTemplate),
                new TemplateProfile("RPG", DefaultRpgTemplate),
                new TemplateProfile("Aventura", DefaultAdventureTemplate),
                new TemplateProfile("Indie", DefaultIndieTemplate),
                new TemplateProfile("Emulacion", DefaultEmulationTemplate)
            };
        }

        private void RepairEmptyDefaultTemplates()
        {
            var defaults = CreateDefaultTemplates();
            foreach (var template in Templates)
            {
                if (template == null || !string.IsNullOrWhiteSpace(template.Template))
                {
                    continue;
                }

                var defaultTemplate = defaults.FirstOrDefault(x => string.Equals(x.Name, template.Name, StringComparison.OrdinalIgnoreCase));
                if (defaultTemplate != null)
                {
                    template.Template = defaultTemplate.Template;
                }
            }
        }

        private void DeduplicateTemplates()
        {
            var cleaned = new ObservableCollection<TemplateProfile>();
            foreach (var template in Templates.Where(x => x != null))
            {
                var name = string.IsNullOrWhiteSpace(template.Name) ? "Plantilla" : template.Name.Trim();
                var existing = cleaned.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    template.Name = name;
                    cleaned.Add(template);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.Template) && !string.IsNullOrWhiteSpace(template.Template))
                {
                    existing.Template = template.Template;
                }
            }

            if (cleaned.Count == 0)
            {
                Templates = CreateDefaultTemplates();
                return;
            }

            if (cleaned.Count != Templates.Count)
            {
                Templates = cleaned;
            }
        }

        public List<string> GetBlacklistTerms()
        {
            return SplitTerms(Blacklist);
        }

        public void ApplyProviderPreset()
        {
            if (ProviderPreset == ProviderOpenAI)
            {
                Endpoint = "https://api.openai.com/v1/chat/completions";
                Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4.1-mini" : Model;
            }
            else if (ProviderPreset == ProviderLmStudio)
            {
                Endpoint = "http://localhost:1234/v1/chat/completions";
                Model = "local-model";
            }
            else if (ProviderPreset == ProviderOllama)
            {
                Endpoint = "http://localhost:11434/v1/chat/completions";
                Model = "llama3.1";
            }
            else if (ProviderPreset == ProviderGemini)
            {
                Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                Model = "gemini-2.5-flash";
            }
            else if (ProviderPreset == ProviderClaude)
            {
                Endpoint = "https://api.anthropic.com/v1/messages";
                Model = "claude-sonnet-4-5";
            }
            else if (ProviderPreset == ProviderOpenRouter)
            {
                Endpoint = "https://openrouter.ai/api/v1/chat/completions";
                Model = "openrouter/auto";
            }
            else if (ProviderPreset == ProviderGroq)
            {
                Endpoint = "https://api.groq.com/openai/v1/chat/completions";
                Model = "llama-3.1-8b-instant";
            }
        }

        public void ApplyFreeLocalPreset(string provider)
        {
            ProviderPreset = provider;
            ApplyProviderPreset();
            ApiKey = string.Empty;
            EnableLocalFallback = true;
        }

        public MetaDataIASettings CreateLocalFallbackSettings(string provider)
        {
            var clone = Serialization.GetClone(this);
            clone.ProviderPreset = provider;
            clone.ApiKey = string.Empty;
            clone.EnableLocalFallback = false;

            if (provider == ProviderLmStudio)
            {
                clone.Endpoint = "http://localhost:1234/v1/chat/completions";
                clone.Model = string.IsNullOrWhiteSpace(LmStudioFallbackModel) ? "local-model" : LmStudioFallbackModel;
            }
            else if (provider == ProviderOllama)
            {
                clone.Endpoint = "http://localhost:11434/v1/chat/completions";
                clone.Model = string.IsNullOrWhiteSpace(OllamaFallbackModel) ? "llama3.1" : OllamaFallbackModel;
            }

            return clone;
        }

        private static string ResolveTemplateName(string rules, IEnumerable<string> values)
        {
            if (string.IsNullOrWhiteSpace(rules) || values == null)
            {
                return null;
            }

            var valueList = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            foreach (var rawLine in rules.Replace("\r", string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                {
                    continue;
                }

                var parts = line.Split(new[] { '=' }, 2);
                var key = parts[0].Trim();
                var templateName = parts[1].Trim();
                if (valueList.Any(x => x.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return templateName;
                }
            }

            return null;
        }

        private static List<string> SplitTerms(string terms)
        {
            if (string.IsNullOrWhiteSpace(terms))
            {
                return new List<string>();
            }

            return terms
                .Replace("\r", string.Empty)
                .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [DontSerialize]
        public List<string> ApplyModeOptions
        {
            get { return new List<string> { ApplySkip, ApplyEmptyOnly, ApplyAppend, ApplyOverwrite }; }
        }

        [DontSerialize]
        public List<string> ProviderPresetOptions
        {
            get { return new List<string> { ProviderOpenAI, ProviderGemini, ProviderClaude, ProviderLmStudio, ProviderOllama, ProviderOpenRouter, ProviderGroq, ProviderCustom }; }
        }

        [DontSerialize]
        public List<LanguageOption> LanguageOptions
        {
            get
            {
                return new List<LanguageOption>
                {
                    new LanguageOption("es", "Espanol (es)"),
                    new LanguageOption("en", "English (en)"),
                    new LanguageOption("pl", "Polski (pl)"),
                    new LanguageOption("fr", "Francais (fr)"),
                    new LanguageOption("de", "Deutsch (de)"),
                    new LanguageOption("it", "Italiano (it)"),
                    new LanguageOption("pt", "Portugues (pt)"),
                    new LanguageOption("nl", "Nederlands (nl)"),
                    new LanguageOption("ru", "Russian (ru)"),
                    new LanguageOption("uk", "Ukrainian (uk)"),
                    new LanguageOption("ja", "Japanese (ja)"),
                    new LanguageOption("ko", "Korean (ko)"),
                    new LanguageOption("zh", "Chinese (zh)"),
                    new LanguageOption("sv", "Svenska (sv)"),
                    new LanguageOption("no", "Norsk (no)"),
                    new LanguageOption("da", "Dansk (da)"),
                    new LanguageOption("fi", "Suomi (fi)"),
                    new LanguageOption("tr", "Turkce (tr)"),
                    new LanguageOption("cs", "Cestina (cs)"),
                    new LanguageOption("hu", "Magyar (hu)"),
                    new LanguageOption("ro", "Romana (ro)")
                };
            }
        }

        [DontSerialize]
        public string ProviderKeyHelp
        {
            get
            {
                if (ProviderPreset == ProviderOpenAI)
                {
                    return "OpenAI: crea la clave en platform.openai.com/api-keys. ChatGPT Plus/Pro no incluye uso de API; la API usa facturacion separada en la plataforma de OpenAI.";
                }

                if (ProviderPreset == ProviderGemini)
                {
                    return "Google Gemini: crea la clave en Google AI Studio. Gemini Pro/Google AI Pro de la app no aumenta automaticamente la cuota ni la disponibilidad de la API; la API usa sus propios tiers y limites.";
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return "Claude Anthropic: crea la clave en Anthropic Console. La suscripcion de claude.ai y la API son productos separados.";
                }

                if (ProviderPreset == ProviderOpenRouter)
                {
                    return "OpenRouter: crea la clave en OpenRouter. Puedes elegir modelos gratuitos si el modelo termina en :free o aparece como Free, pero tienen limites y disponibilidad variable.";
                }

                if (ProviderPreset == ProviderGroq)
                {
                    return "Groq: crea la clave en GroqCloud Console. Tiene opcion de empezar gratis, normalmente con limites de uso.";
                }

                if (ProviderPreset == ProviderLmStudio)
                {
                    return "LM Studio local: no necesitas API key. Abre LM Studio, carga un modelo y activa el servidor local en la pestana Developer.";
                }

                if (ProviderPreset == ProviderOllama)
                {
                    return "Ollama local: no necesitas API key. Instala Ollama, descarga un modelo con 'ollama pull' y deja el servicio local arrancado.";
                }

                return "Proveedor personalizado: usa la URL, modelo y API key que indique ese proveedor compatible con la API de OpenAI.";
            }
        }

        [DontSerialize]
        public string ProviderKeyUrl
        {
            get
            {
                if (ProviderPreset == ProviderOpenAI)
                {
                    return "https://platform.openai.com/api-keys";
                }

                if (ProviderPreset == ProviderGemini)
                {
                    return "https://aistudio.google.com/app/apikey";
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return "https://console.anthropic.com/settings/keys";
                }

                if (ProviderPreset == ProviderOpenRouter)
                {
                    return "https://openrouter.ai/settings/keys";
                }

                if (ProviderPreset == ProviderGroq)
                {
                    return "https://console.groq.com/keys";
                }

                if (ProviderPreset == ProviderLmStudio)
                {
                    return "https://lmstudio.ai/docs/developer/core/server";
                }

                if (ProviderPreset == ProviderOllama)
                {
                    return "https://docs.ollama.com/api";
                }

                return string.Empty;
            }
        }

        [DontSerialize]
        public string ProviderBillingHelp
        {
            get
            {
                if (ProviderPreset == ProviderOpenAI)
                {
                    return "Para usar tu ChatGPT Plus en este plugin no basta con iniciar sesion: necesitas una API key de OpenAI Platform y saldo/facturacion de API. Plus solo te da ventajas dentro de ChatGPT.";
                }

                if (ProviderPreset == ProviderLmStudio || ProviderPreset == ProviderOllama)
                {
                    return "Opcion recomendada si no quieres pagar: el coste es tu propio hardware. La velocidad y calidad dependen del modelo y del PC.";
                }

                if (ProviderPreset == ProviderGemini || ProviderPreset == ProviderGroq || ProviderPreset == ProviderOpenRouter)
                {
                    return "Puede funcionar sin pagar si eliges un modelo/cuota gratuitos, pero si superas limites o hay alta demanda tendras que esperar, cambiar a un modelo mas disponible o activar facturacion segun el proveedor.";
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return "Claude API suele requerir facturacion propia; tener Claude Pro en la web no equivale a tener saldo de API.";
                }

                return "Consulta las condiciones del proveedor antes de procesar bibliotecas grandes.";
            }
        }

        [DontSerialize]
        public List<string> ToneOptions
        {
            get { return new List<string> { "Neutral", "Enciclopedico", "Tienda", "Critico", "Breve", "Gamer", "Entusiasta", "Retro", "Tecnico", "Familiar" }; }
        }

        [DontSerialize]
        public List<string> LengthOptions
        {
            get { return new List<string> { "Corta", "Media", "Larga", "Extra larga" }; }
        }

        [DontSerialize]
        public List<string> ExistingMetadataModeOptions
        {
            get { return new List<string> { "Usar como contexto", "Normalizar", "Ignorar" }; }
        }

        [DontSerialize]
        public List<string> MediaProviderOptions
        {
            get { return new List<string> { "Varias fuentes", MediaProviderSteamGridDb }; }
        }

        [DontSerialize]
        public List<string> CoverImagePresetOptions
        {
            get { return new List<string> { CoverPresetPlayniteVertical, CoverPresetOriginal, CoverPresetSquare, CoverPresetHorizontal }; }
        }

        [DontSerialize]
        public List<string> IconPresetOptions
        {
            get { return new List<string> { IconPresetOriginal, IconPresetSquare, IconPresetRounded, IconPresetCircle }; }
        }

        [DontSerialize]
        public List<string> BackgroundImagePresetOptions
        {
            get { return new List<string> { BackgroundPresetSteamHero, BackgroundPresetSteamHeroSmall, BackgroundPresetFullHd, BackgroundPresetQhd, BackgroundPreset4K, BackgroundPresetOriginal }; }
        }

        [DontSerialize]
        public List<string> BackgroundLogoPreferenceOptions
        {
            get { return new List<string> { BackgroundLogoAny, BackgroundLogoPreferNoLogo, BackgroundLogoPreferLogo }; }
        }

        [DontSerialize]
        public string SteamGridDbHelp
        {
            get { return "El plugin mezcla fuentes de media: Steam oficial cuando el juego tiene AppID de Steam, SteamGridDB como fuente comunitaria si configuras su API key, y capturas oficiales de Steam como fondo alternativo. En automatico prioriza assets oficiales y despues aplica formato, logo, puntuacion y filtros."; }
        }

        [DontSerialize]
        public bool IsMediaConfigured
        {
            get { return true; }
        }

        [DontSerialize]
        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Endpoint) &&
                       !string.IsNullOrWhiteSpace(Model);
            }
        }

        private static int Clamp(int value)
        {
            return Math.Max(1, Math.Min(50, value));
        }
    }

    public class MetaDataIASettingsViewModel : ObservableObject, ISettings
    {
        private readonly MetaDataIAPlugin plugin;
        private MetaDataIASettings editingClone;

        private MetaDataIASettings settings;
        private TemplateProfile selectedTemplate;
        private string selectedTemplateNameText;
        private string selectedTemplateBodyText;
        private bool loadingSelectedTemplate;

        public MetaDataIASettings Settings
        {
            get { return settings; }
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public TemplateProfile SelectedTemplate
        {
            get { return selectedTemplate; }
            set
            {
                SyncSelectedTemplate();
                selectedTemplate = value;
                LoadSelectedTemplateText();

                OnPropertyChanged();
            }
        }

        public string SelectedTemplateNameText
        {
            get { return selectedTemplateNameText; }
            set
            {
                SetValue(ref selectedTemplateNameText, value);
                if (!loadingSelectedTemplate && selectedTemplate != null)
                {
                    selectedTemplate.Name = selectedTemplateNameText ?? string.Empty;
                    Settings.ActiveTemplateName = selectedTemplate.Name;
                    OnPropertyChanged("SelectedTemplate");
                }
            }
        }

        public string SelectedTemplateBodyText
        {
            get { return selectedTemplateBodyText; }
            set
            {
                SetValue(ref selectedTemplateBodyText, value);
                if (!loadingSelectedTemplate && selectedTemplate != null)
                {
                    selectedTemplate.Template = selectedTemplateBodyText ?? string.Empty;
                    Settings.DescriptionTemplate = selectedTemplate.Template;
                }
            }
        }

        public MetaDataIASettingsViewModel(MetaDataIAPlugin plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<MetaDataIASettings>();
            Settings = savedSettings ?? new MetaDataIASettings();
            Settings.EnsureDefaults();
            SelectedTemplate = Settings.GetActiveTemplate();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
            SelectedTemplate = Settings == null ? null : Settings.GetActiveTemplate();
        }

        public void EndEdit()
        {
            SyncSelectedTemplate();

            plugin.SavePluginSettings(Settings);
        }

        public void SyncSelectedTemplate()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.EnsureDefaults();
            if (SelectedTemplate == null)
            {
                selectedTemplate = Settings.GetActiveTemplate();
                LoadSelectedTemplateText();
            }

            if (SelectedTemplate != null)
            {
                SelectedTemplate.Name = SelectedTemplateNameText ?? SelectedTemplate.Name;
                SelectedTemplate.Template = SelectedTemplateBodyText ?? SelectedTemplate.Template;
                Settings.ActiveTemplateName = SelectedTemplate.Name;
                Settings.DescriptionTemplate = SelectedTemplate.Template;
            }
        }

        private void LoadSelectedTemplateText()
        {
            loadingSelectedTemplate = true;
            SelectedTemplateNameText = selectedTemplate == null ? string.Empty : selectedTemplate.Name;
            SelectedTemplateBodyText = selectedTemplate == null ? string.Empty : selectedTemplate.Template;
            loadingSelectedTemplate = false;
        }

        public void AddTemplate()
        {
            Settings.EnsureDefaults();
            var index = 1;
            var name = "Nueva plantilla";
            while (Settings.Templates.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                index++;
                name = "Nueva plantilla " + index;
            }

            var profile = new TemplateProfile(name, MetaDataIASettings.DefaultMediumTemplate);
            Settings.Templates.Add(profile);
            SelectedTemplate = profile;
        }

        public void DeleteSelectedTemplate()
        {
            Settings.EnsureDefaults();
            if (SelectedTemplate == null || Settings.Templates.Count <= 1)
            {
                return;
            }

            var index = Settings.Templates.IndexOf(SelectedTemplate);
            Settings.Templates.Remove(SelectedTemplate);
            SelectedTemplate = Settings.Templates[Math.Max(0, Math.Min(index, Settings.Templates.Count - 1))];
        }

        public void RestoreDefaultTemplates()
        {
            Settings.ResetTemplates();
            SelectedTemplate = Settings.GetActiveTemplate();
        }

        public bool VerifySettings(out List<string> errors)
        {
            SyncSelectedTemplate();
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.Endpoint))
            {
                errors.Add("El endpoint es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Model))
            {
                errors.Add("El modelo es obligatorio.");
            }

            if (Settings.MaxListItems < 1 || Settings.MaxListItems > 20)
            {
                errors.Add("El maximo de elementos debe estar entre 1 y 20.");
            }

            if (string.IsNullOrWhiteSpace(Settings.DescriptionTemplate))
            {
                errors.Add("La plantilla de descripcion es obligatoria.");
            }

            return errors.Count == 0;
        }
    }
}
