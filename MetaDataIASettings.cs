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

        public string Name
        {
            get { return name; }
            set
            {
                SetValue(ref name, value);
                OnPropertyChanged("DisplayName");
            }
        }

        public string DisplayName { get { return LocalizeTemplateName(Name); } }
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
            return DisplayName;
        }

        private static string LocalizeTemplateName(string value)
        {
            if (string.Equals(value, "Corta", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateShort", "Short");
            }

            if (string.Equals(value, "Media", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateMedium", "Medium");
            }

            if (string.Equals(value, "Larga", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateLong", "Long");
            }

            if (string.Equals(value, "RPG", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateRpg", "RPG");
            }

            if (string.Equals(value, "Aventura", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateAdventure", "Adventure");
            }

            if (string.Equals(value, "Indie", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateIndie", "Indie");
            }

            if (string.Equals(value, "Emulacion", StringComparison.OrdinalIgnoreCase))
            {
                return PluginLocalization.GetString("MTDA_TemplateEmulation", "Emulation");
            }

            return value ?? string.Empty;
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

    public class LocalizedOption
    {
        public string Value { get; set; }
        public string DisplayName { get; set; }

        public LocalizedOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class MetaDataIASettings : ObservableObject
    {
        public const string ApplySkip = "No tocar";
        public const string ApplyEmptyOnly = "Solo si esta vacio";
        public const string ApplyAppend = "Anadir sin borrar";

        [DontSerialize]
        public string AboutVersionAuthor
        {
            get { return "MetadataAI " + typeof(MetaDataIAPlugin).Assembly.GetName().Version.ToString(3) + " · Narian"; }
        }

        public const string ApplyOverwrite = "Sobrescribir";
        public const string ProviderOpenAI = "OpenAI";
        public const string ProviderLmStudio = "LM Studio local";
        public const string ProviderOllama = "Ollama local";
        public const string ProviderGemini = "Google Gemini";
        public const string ProviderClaude = "Claude Anthropic";
        public const string ProviderOpenRouter = "OpenRouter";
        public const string ProviderOpenRouterFree = "OpenRouter Free";
        public const string ProviderGroq = "Groq";
        public const string ProviderCerebras = "Cerebras";
        public const string ProviderMistral = "Mistral AI";
        public const string ProviderCustom = "Personalizado compatible con OpenAI";

        private string providerPreset = ProviderGroq;
        private string endpoint = "https://api.groq.com/openai/v1/chat/completions";
        private string apiKey = string.Empty;
        private string model = "llama-3.1-8b-instant";
        private string language = "es";
        private bool showAdvancedOptions = false;
        private string descriptionTemplate = DefaultMediumTemplate;
        private ObservableCollection<TemplateProfile> templates;
        private string activeTemplateName = "Media";
        private bool enableTemplateRules = false;
        private string genreTemplateRules = "RPG=RPG\nRol=RPG\nAventura=Aventura\nIndie=Indie\nEmulacion=Emulacion\nRetro=Emulacion";
        private string platformTemplateRules = string.Empty;
        private string sourceTemplateRules = string.Empty;
        private string vocabularyMemory = string.Empty;
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
        private bool generateAgeRatings = false;
        private bool generateRegions = false;
        private bool generateCategories = true;
        private bool generateSortingName = true;
        private bool generateLinks = false;
        private bool generateReleaseDate = true;
        private bool generateSeries = true;
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
        private string releaseDateApplyMode = ApplyEmptyOnly;
        private string seriesApplyMode = ApplyEmptyOnly;
        private int maxGenres = 4;
        private int maxTags = 10;
        private int maxFeatures = 8;
        private int maxDevelopers = 1;
        private int maxPublishers = 1;
        private int maxAgeRatings = 2;
        private int maxRegions = 3;
        private int maxCategories = 6;
        private int maxLinks = 5;
        private int maxSeries = 1;
        private string tagPrefix = string.Empty;
        private string categoryPrefix = string.Empty;
        private string blacklist = string.Empty;
        private bool preferExistingGenres = false;
        private bool preferExistingTags = false;
        private bool preferExistingFeatures = false;
        private bool preferExistingCategories = true;
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
        private bool useOfficialStoreContext = false;
        private bool strictCompanyAgeRegion = true;
        private bool enableLocalFallback = true;
        private bool tryLmStudioFallback = true;
        private bool tryOllamaFallback = true;
        private string lmStudioFallbackModel = "local-model";
        private string ollamaFallbackModel = "llama3.1";
        private bool companyLimitDefaultsMigrated = false;
        private bool safeDefaultsMigrated = false;
        private bool setupWizardCompleted = false;
        private bool setupWizardMigrationApplied = false;
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
        private string coverImagePreset = CoverPresetPlayniteDefined;
        private string iconPreset = IconPresetOriginal;
        private string backgroundImagePreset = BackgroundPresetSteamHero;
        private string backgroundLogoPreference = BackgroundLogoAny;
        private string mediaCoverExcludedSearchTerms = string.Empty;
        private string mediaIconExcludedSearchTerms = string.Empty;
        private string mediaBackgroundExcludedSearchTerms = string.Empty;
        private string mediaLogoExcludedSearchTerms = string.Empty;
        private int mediaSearchMaxResults = 50;
        private bool mediaAvoidNsfw = true;
        private bool mediaAvoidBlurred = true;
        private bool mediaPreferOfficial = true;
        private bool mediaAvoidConsoleCovers = true;
        private bool iconSquarePreferGrid = true;
        private bool mediaUseSteamOfficial = true;
        private bool mediaUseSteamScreenshots = true;
        private bool useOriginIntegrationForMedia = true;
        private bool useOriginIntegrationAsAiContext = true;
        private bool useOriginIntegrationForFactualMetadata = true;
        private List<Guid> disabledOriginIntegrationIds = new List<Guid>();
        private bool originIntegrationPriorityMigrated = false;
        private bool mediaUsePsnStore = false;
        private bool mediaUseXboxStore = false;
        private bool mediaUseEpicStore = false;
        private bool mediaUseSteamGridDb = true;
        private bool mediaUseSteamGridDbBackgroundGrids = true;
        private bool mediaUseRawg = false;
        private string rawgApiKey = string.Empty;
        private bool mediaUseWallhaven = false;
        private bool mediaUseWebSearch = true;
        private string mediaPickerViewMode = MediaPickerViewGrid;
        private bool mediaUseScreenScraper = false;
        private string screenScraperUserName = string.Empty;
        private string screenScraperPassword = string.Empty;
        private string screenScraperDeveloperId = string.Empty;
        private string screenScraperDeveloperPassword = string.Empty;
        private bool mediaUseGiantBomb = false;
        private string giantBombApiKey = string.Empty;
        private bool mediaUseMobyGames = false;
        private string mobyGamesApiKey = string.Empty;
        private bool mediaUseIgdb = false;
        private string igdbClientId = string.Empty;
        private string igdbClientSecret = string.Empty;
        private string igdbAccessToken = string.Empty;
        private string mediaCoverSourcePriority = DefaultCoverSourcePriority;
        private string mediaIconSourcePriority = DefaultIconSourcePriority;
        private string mediaBackgroundSourcePriority = DefaultBackgroundSourcePriority;
        private string mediaAutomaticPriority = MediaPriorityBalanced;
        private string coverCropAnchor = CropAnchorCenter;
        private string backgroundCropAnchor = CropAnchorCenter;
        private string processedImageQuality = ImageQualityBalanced;
        private bool mediaRepairOnlyWhenBetter = true;
        private bool mediaMinimumQualityEnabled = true;
        private int mediaMinimumCoverWidth = 600;
        private int mediaMinimumIconWidth = 256;
        private int mediaMinimumBackgroundWidth = 1920;
        private bool enableExtraMetadataLoaderLogos = false;

        public const string DefaultShortTemplate = "<p>{short}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}";
        public const string DefaultMediumTemplate = "<h3>Descripcion breve</h3>\n<p>{short}</p>\n\n<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}\n\n<h3>Modos de juego</h3>\n<p>{playModes}</p>\n\n<h3>Duracion estimada</h3>\n<p>{estimatedLength}</p>\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultLongTemplate = "<h3>Descripcion breve</h3>\n<p>{short}</p>\n\n<h3>Premisa</h3>\n<p>{premise}</p>\n\n<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Jugabilidad</h3>\n<p>{gameplay}</p>\n\n<h3>Tono y ambientacion</h3>\n<p>{tone}</p>\n<p>{setting}</p>\n\n<h3>Perspectiva y modos</h3>\n<p>{perspective}</p>\n<p>{playModes}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}\n\n<h3>Duracion estimada</h3>\n<p>{estimatedLength}</p>\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>\n\n<h3>Notas</h3>\n<p>{notes}</p>";
        private const string LegacyDefaultLongTemplate = "<h3>Descripcion breve</h3>\n<p>{short}</p>\n\n<h3>Premisa</h3>\n<p>{premise}</p>\n\n<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Jugabilidad</h3>\n<p>{gameplay}</p>\n\n<h3>Tono y ambientacion</h3>\n<p>{tone}</p>\n<p>{setting}</p>\n\n<h3>Perspectiva y modos</h3>\n<p>{perspective}</p>\n<p>{playModes}</p>\n\n<h3>Caracteristicas principales</h3>\n{features}\n\n<h3>Duracion estimada</h3>\n<p>{estimatedLength}</p>\n\n<h3>Juegos similares</h3>\n<p>{similarGames}</p>\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>\n\n<h3>Notas</h3>\n<p>{notes}</p>";
        public const string DefaultRpgTemplate = "<h3>Sinopsis</h3>\n<p>{synopsis}</p>\n\n<h3>Rol y progresion</h3>\n<p>{gameplay}</p>\n\n<h3>Mundo y tono</h3>\n<p>{setting}</p>\n<p>{tone}</p>\n\n<h3>Caracteristicas RPG</h3>\n{features}\n\n<h3>Recomendado para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultAdventureTemplate = "<h3>Premisa</h3>\n<p>{premise}</p>\n\n<h3>Aventura</h3>\n<p>{synopsis}</p>\n\n<h3>Exploracion y ritmo</h3>\n<p>{gameplay}</p>\n\n<h3>Caracteristicas</h3>\n{features}\n\n<h3>Ideal para</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultIndieTemplate = "<h3>Resumen</h3>\n<p>{short}</p>\n\n<h3>Propuesta</h3>\n<p>{premise}</p>\n\n<h3>Estilo</h3>\n<p>{tone}</p>\n\n<h3>Elementos clave</h3>\n{features}\n\n<h3>Para quien es</h3>\n<p>{recommendedFor}</p>";
        public const string DefaultEmulationTemplate = "<h3>Resumen</h3>\n<p>{short}</p>\n\n<h3>Contexto</h3>\n<p>{synopsis}</p>\n\n<h3>Jugabilidad</h3>\n<p>{gameplay}</p>\n\n<h3>Datos utiles</h3>\n<p>Plataforma/perspectiva: {perspective}</p>\n<p>Modos: {playModes}</p>\n\n<h3>Caracteristicas</h3>\n{features}";
        public const string MediaProviderSteamGridDb = "SteamGridDB";
        public const string CoverPresetPlayniteDefined = "Definido por Playnite";
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
        public const string BackgroundPresetHd = "Pantalla completa HD (1280x720)";
        public const string BackgroundPresetFullHd = "Pantalla completa Full HD (1920x1080)";
        public const string BackgroundPresetQhd = "Pantalla completa QHD (2560x1440)";
        public const string BackgroundPreset4K = "Pantalla completa 4K (3840x2160)";
        public const string MediaPickerViewGrid = "Grid";
        public const string MediaPickerViewList = "List";
        public const string BackgroundLogoAny = "Cualquiera";
        public const string BackgroundLogoPreferNoLogo = "Preferir sin logo";
        public const string BackgroundLogoPreferLogo = "Preferir con logo";
        public const string MediaPriorityBalanced = "Equilibrada";
        public const string MediaPrioritySourceFirst = "Fuente primero";
        public const string MediaPriorityResolutionFirst = "Resolucion primero";
        public const string MediaPriorityStrictQuality = "Calidad estricta";
        public const string CropAnchorCenter = "Centro";
        public const string CropAnchorTop = "Arriba";
        public const string CropAnchorBottom = "Abajo";
        public const string CropAnchorLeft = "Izquierda";
        public const string CropAnchorRight = "Derecha";
        public const string CropAnchorTopLeft = "Arriba izquierda";
        public const string CropAnchorTopRight = "Arriba derecha";
        public const string CropAnchorBottomLeft = "Abajo izquierda";
        public const string CropAnchorBottomRight = "Abajo derecha";
        public const string ImageQualitySpaceSaving = "Ahorro de espacio";
        public const string ImageQualityBalanced = "Equilibrada";
        public const string ImageQualityHigh = "Alta";
        public const string ImageQualityMaximum = "Maxima";
        public const string SourceOriginIntegration = "Integracion de origen";
        public const string DefaultCoverSourcePriority = "Integracion de origen, Steam oficial, PlayStation Store, Xbox Store, Epic Store, SteamGridDB, IGDB, ScreenScraper, RAWG, Giant Bomb, MobyGames";
        public const string DefaultIconSourcePriority = "SteamGridDB, ScreenScraper, Integracion de origen, Steam oficial, PlayStation Store, Xbox Store, Epic Store";
        public const string DefaultBackgroundSourcePriority = "Integracion de origen, Steam oficial, Steam capturas, PlayStation Store, Xbox Store, Epic Store, SteamGridDB, ScreenScraper, RAWG, Wallhaven, IGDB, Giant Bomb, MobyGames";

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
                OnPropertyChanged("ProviderUsageUrl");
                OnPropertyChanged("ProviderBillingHelp");
                OnPropertyChanged("ShowEndpointEditor");
                OnPropertyChanged("CanRestoreProviderEndpoint");
            }
        }
        public string Endpoint { get { return endpoint; } set { SetValue(ref endpoint, value); } }
        public string ApiKey { get { return apiKey; } set { SetValue(ref apiKey, value); } }
        public string Model { get { return model; } set { SetValue(ref model, value); } }
        public string Language { get { return language; } set { SetValue(ref language, value); } }
        public bool ShowAdvancedOptions
        {
            get { return showAdvancedOptions; }
            set
            {
                if (showAdvancedOptions == value)
                {
                    return;
                }

                SetValue(ref showAdvancedOptions, value);
                OnPropertyChanged("ShowEndpointEditor");
            }
        }

        [DontSerialize]
        public bool ShowEndpointEditor
        {
            get { return ShowAdvancedOptions || ProviderPreset == ProviderCustom; }
        }

        [DontSerialize]
        public bool CanRestoreProviderEndpoint
        {
            get { return ProviderPreset != ProviderCustom; }
        }
        public string DescriptionTemplate { get { return descriptionTemplate; } set { SetValue(ref descriptionTemplate, value); } }
        public ObservableCollection<TemplateProfile> Templates { get { return templates; } set { SetValue(ref templates, value); } }
        public string ActiveTemplateName { get { return activeTemplateName; } set { SetValue(ref activeTemplateName, value); } }
        public bool EnableTemplateRules { get { return enableTemplateRules; } set { SetValue(ref enableTemplateRules, value); } }
        public string GenreTemplateRules { get { return genreTemplateRules; } set { SetValue(ref genreTemplateRules, value); } }
        public string PlatformTemplateRules { get { return platformTemplateRules; } set { SetValue(ref platformTemplateRules, value); } }
        public string SourceTemplateRules { get { return sourceTemplateRules; } set { SetValue(ref sourceTemplateRules, value); } }
        public string VocabularyMemory { get { return vocabularyMemory; } set { SetValue(ref vocabularyMemory, value); } }
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
        public bool GenerateReleaseDate { get { return generateReleaseDate; } set { SetValue(ref generateReleaseDate, value); } }
        public bool GenerateSeries { get { return generateSeries; } set { SetValue(ref generateSeries, value); } }
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
        public string ReleaseDateApplyMode { get { return releaseDateApplyMode; } set { SetValue(ref releaseDateApplyMode, value); } }
        public string SeriesApplyMode { get { return seriesApplyMode; } set { SetValue(ref seriesApplyMode, value); } }
        public int MaxGenres { get { return maxGenres; } set { SetValue(ref maxGenres, Clamp(value)); } }
        public int MaxTags { get { return maxTags; } set { SetValue(ref maxTags, Clamp(value)); } }
        public int MaxFeatures { get { return maxFeatures; } set { SetValue(ref maxFeatures, Clamp(value)); } }
        public int MaxDevelopers { get { return maxDevelopers; } set { SetValue(ref maxDevelopers, Clamp(value)); } }
        public int MaxPublishers { get { return maxPublishers; } set { SetValue(ref maxPublishers, Clamp(value)); } }
        public int MaxAgeRatings { get { return maxAgeRatings; } set { SetValue(ref maxAgeRatings, Clamp(value)); } }
        public int MaxRegions { get { return maxRegions; } set { SetValue(ref maxRegions, Clamp(value)); } }
        public int MaxCategories { get { return maxCategories; } set { SetValue(ref maxCategories, Clamp(value)); } }
        public int MaxLinks { get { return maxLinks; } set { SetValue(ref maxLinks, Clamp(value)); } }
        public int MaxSeries { get { return maxSeries; } set { SetValue(ref maxSeries, Math.Max(1, Math.Min(5, value))); } }
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
        public bool UseOfficialStoreContext { get { return useOfficialStoreContext; } set { SetValue(ref useOfficialStoreContext, value); } }
        public bool StrictCompanyAgeRegion { get { return strictCompanyAgeRegion; } set { SetValue(ref strictCompanyAgeRegion, value); } }
        public bool EnableLocalFallback { get { return enableLocalFallback; } set { SetValue(ref enableLocalFallback, value); } }
        public bool TryLmStudioFallback { get { return tryLmStudioFallback; } set { SetValue(ref tryLmStudioFallback, value); } }
        public bool TryOllamaFallback { get { return tryOllamaFallback; } set { SetValue(ref tryOllamaFallback, value); } }
        public string LmStudioFallbackModel { get { return lmStudioFallbackModel; } set { SetValue(ref lmStudioFallbackModel, value); } }
        public string OllamaFallbackModel { get { return ollamaFallbackModel; } set { SetValue(ref ollamaFallbackModel, value); } }
        public bool CompanyLimitDefaultsMigrated { get { return companyLimitDefaultsMigrated; } set { SetValue(ref companyLimitDefaultsMigrated, value); } }
        public bool SafeDefaultsMigrated { get { return safeDefaultsMigrated; } set { SetValue(ref safeDefaultsMigrated, value); } }
        public bool SetupWizardCompleted { get { return setupWizardCompleted; } set { SetValue(ref setupWizardCompleted, value); } }
        public bool SetupWizardMigrationApplied { get { return setupWizardMigrationApplied; } set { SetValue(ref setupWizardMigrationApplied, value); } }
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
        public string MediaCoverExcludedSearchTerms { get { return mediaCoverExcludedSearchTerms; } set { SetValue(ref mediaCoverExcludedSearchTerms, value); } }
        public string MediaIconExcludedSearchTerms { get { return mediaIconExcludedSearchTerms; } set { SetValue(ref mediaIconExcludedSearchTerms, value); } }
        public string MediaBackgroundExcludedSearchTerms { get { return mediaBackgroundExcludedSearchTerms; } set { SetValue(ref mediaBackgroundExcludedSearchTerms, value); } }
        public string MediaLogoExcludedSearchTerms { get { return mediaLogoExcludedSearchTerms; } set { SetValue(ref mediaLogoExcludedSearchTerms, value); } }
        public int MediaSearchMaxResults { get { return mediaSearchMaxResults; } set { SetValue(ref mediaSearchMaxResults, Math.Max(1, Math.Min(100, value))); } }
        public bool MediaAvoidNsfw { get { return mediaAvoidNsfw; } set { SetValue(ref mediaAvoidNsfw, value); } }
        public bool MediaAvoidBlurred { get { return mediaAvoidBlurred; } set { SetValue(ref mediaAvoidBlurred, value); } }
        public bool MediaPreferOfficial { get { return mediaPreferOfficial; } set { SetValue(ref mediaPreferOfficial, value); } }
        public bool MediaAvoidConsoleCovers { get { return mediaAvoidConsoleCovers; } set { SetValue(ref mediaAvoidConsoleCovers, value); } }
        public bool IconSquarePreferGrid { get { return iconSquarePreferGrid; } set { SetValue(ref iconSquarePreferGrid, value); } }
        public bool MediaUseSteamOfficial { get { return mediaUseSteamOfficial; } set { SetValue(ref mediaUseSteamOfficial, value); } }
        public bool MediaUseSteamScreenshots { get { return mediaUseSteamScreenshots; } set { SetValue(ref mediaUseSteamScreenshots, value); } }
        public bool UseOriginIntegrationForMedia { get { return useOriginIntegrationForMedia; } set { SetValue(ref useOriginIntegrationForMedia, value); } }
        public bool UseOriginIntegrationAsAiContext { get { return useOriginIntegrationAsAiContext; } set { SetValue(ref useOriginIntegrationAsAiContext, value); } }
        public bool UseOriginIntegrationForFactualMetadata { get { return useOriginIntegrationForFactualMetadata; } set { SetValue(ref useOriginIntegrationForFactualMetadata, value); } }
        public List<Guid> DisabledOriginIntegrationIds { get { return disabledOriginIntegrationIds; } set { SetValue(ref disabledOriginIntegrationIds, value); } }
        public bool OriginIntegrationPriorityMigrated { get { return originIntegrationPriorityMigrated; } set { SetValue(ref originIntegrationPriorityMigrated, value); } }
        public bool MediaUsePsnStore { get { return mediaUsePsnStore; } set { SetValue(ref mediaUsePsnStore, value); } }
        public bool MediaUseXboxStore { get { return mediaUseXboxStore; } set { SetValue(ref mediaUseXboxStore, value); } }
        public bool MediaUseEpicStore { get { return mediaUseEpicStore; } set { SetValue(ref mediaUseEpicStore, value); } }
        public bool MediaUseSteamGridDb { get { return mediaUseSteamGridDb; } set { SetValue(ref mediaUseSteamGridDb, value); } }
        public bool MediaUseSteamGridDbBackgroundGrids { get { return mediaUseSteamGridDbBackgroundGrids; } set { SetValue(ref mediaUseSteamGridDbBackgroundGrids, value); } }
        public bool MediaUseRawg { get { return mediaUseRawg; } set { SetValue(ref mediaUseRawg, value); } }
        public string RawgApiKey { get { return rawgApiKey; } set { SetValue(ref rawgApiKey, value); } }
        public bool MediaUseWallhaven
        {
            get { return mediaUseWallhaven; }
            set
            {
                SetValue(ref mediaUseWallhaven, value);
                if (value)
                {
                    MediaBackgroundSourcePriority = AppendSourcePriority(MediaBackgroundSourcePriority, "Wallhaven");
                }
            }
        }
        public bool MediaUseWebSearch { get { return mediaUseWebSearch; } set { SetValue(ref mediaUseWebSearch, value); } }
        public string MediaPickerViewMode { get { return mediaPickerViewMode; } set { SetValue(ref mediaPickerViewMode, value); } }
        public bool MediaUseScreenScraper
        {
            get { return mediaUseScreenScraper; }
            set
            {
                SetValue(ref mediaUseScreenScraper, value);
                if (value)
                {
                    MediaCoverSourcePriority = AppendSourcePriority(MediaCoverSourcePriority, "ScreenScraper");
                    MediaIconSourcePriority = AppendSourcePriority(MediaIconSourcePriority, "ScreenScraper");
                    MediaBackgroundSourcePriority = AppendSourcePriority(MediaBackgroundSourcePriority, "ScreenScraper");
                }
            }
        }
        public string ScreenScraperUserName { get { return screenScraperUserName; } set { SetValue(ref screenScraperUserName, value); } }
        public string ScreenScraperPassword { get { return screenScraperPassword; } set { SetValue(ref screenScraperPassword, value); } }
        public string ScreenScraperDeveloperId { get { return screenScraperDeveloperId; } set { SetValue(ref screenScraperDeveloperId, value); } }
        public string ScreenScraperDeveloperPassword { get { return screenScraperDeveloperPassword; } set { SetValue(ref screenScraperDeveloperPassword, value); } }
        public bool MediaUseGiantBomb
        {
            get { return mediaUseGiantBomb; }
            set
            {
                SetValue(ref mediaUseGiantBomb, value);
                if (value)
                {
                    MediaCoverSourcePriority = AppendSourcePriority(MediaCoverSourcePriority, "Giant Bomb");
                    MediaBackgroundSourcePriority = AppendSourcePriority(MediaBackgroundSourcePriority, "Giant Bomb");
                }
            }
        }
        public string GiantBombApiKey { get { return giantBombApiKey; } set { SetValue(ref giantBombApiKey, value); } }
        public bool MediaUseMobyGames { get { return mediaUseMobyGames; } set { SetValue(ref mediaUseMobyGames, value); } }
        public string MobyGamesApiKey { get { return mobyGamesApiKey; } set { SetValue(ref mobyGamesApiKey, value); } }
        public bool MediaUseIgdb { get { return mediaUseIgdb; } set { SetValue(ref mediaUseIgdb, value); } }
        public string IgdbClientId { get { return igdbClientId; } set { SetValue(ref igdbClientId, value); } }
        public string IgdbClientSecret { get { return igdbClientSecret; } set { SetValue(ref igdbClientSecret, value); } }
        public string IgdbAccessToken { get { return igdbAccessToken; } set { SetValue(ref igdbAccessToken, value); } }

        public void ProtectSecretsForStorage()
        {
            ApiKey = SecretProtectionService.Protect(ApiKey);
            SteamGridDbApiKey = SecretProtectionService.Protect(SteamGridDbApiKey);
            RawgApiKey = SecretProtectionService.Protect(RawgApiKey);
            ScreenScraperUserName = SecretProtectionService.Protect(ScreenScraperUserName);
            ScreenScraperPassword = SecretProtectionService.Protect(ScreenScraperPassword);
            ScreenScraperDeveloperId = SecretProtectionService.Protect(ScreenScraperDeveloperId);
            ScreenScraperDeveloperPassword = SecretProtectionService.Protect(ScreenScraperDeveloperPassword);
            GiantBombApiKey = SecretProtectionService.Protect(GiantBombApiKey);
            MobyGamesApiKey = SecretProtectionService.Protect(MobyGamesApiKey);
            IgdbClientId = SecretProtectionService.Protect(IgdbClientId);
            IgdbClientSecret = SecretProtectionService.Protect(IgdbClientSecret);
            IgdbAccessToken = SecretProtectionService.Protect(IgdbAccessToken);
        }

        public bool UnprotectSecretsAfterLoad()
        {
            var succeeded = true;
            string plainText;

            succeeded = SecretProtectionService.TryUnprotect(ApiKey, out plainText) && succeeded;
            ApiKey = plainText;
            succeeded = SecretProtectionService.TryUnprotect(SteamGridDbApiKey, out plainText) && succeeded;
            SteamGridDbApiKey = plainText;
            succeeded = SecretProtectionService.TryUnprotect(RawgApiKey, out plainText) && succeeded;
            RawgApiKey = plainText;
            succeeded = SecretProtectionService.TryUnprotect(ScreenScraperUserName, out plainText) && succeeded;
            ScreenScraperUserName = plainText;
            succeeded = SecretProtectionService.TryUnprotect(ScreenScraperPassword, out plainText) && succeeded;
            ScreenScraperPassword = plainText;
            succeeded = SecretProtectionService.TryUnprotect(ScreenScraperDeveloperId, out plainText) && succeeded;
            ScreenScraperDeveloperId = plainText;
            succeeded = SecretProtectionService.TryUnprotect(ScreenScraperDeveloperPassword, out plainText) && succeeded;
            ScreenScraperDeveloperPassword = plainText;
            succeeded = SecretProtectionService.TryUnprotect(GiantBombApiKey, out plainText) && succeeded;
            GiantBombApiKey = plainText;
            succeeded = SecretProtectionService.TryUnprotect(MobyGamesApiKey, out plainText) && succeeded;
            MobyGamesApiKey = plainText;
            succeeded = SecretProtectionService.TryUnprotect(IgdbClientId, out plainText) && succeeded;
            IgdbClientId = plainText;
            succeeded = SecretProtectionService.TryUnprotect(IgdbClientSecret, out plainText) && succeeded;
            IgdbClientSecret = plainText;
            succeeded = SecretProtectionService.TryUnprotect(IgdbAccessToken, out plainText) && succeeded;
            IgdbAccessToken = plainText;

            return succeeded;
        }
        public string MediaCoverSourcePriority
        {
            get { return mediaCoverSourcePriority; }
            set
            {
                SetValue(ref mediaCoverSourcePriority, value);
                OnPropertyChanged("MediaCoverSourcePrioritySummary");
            }
        }

        public string MediaIconSourcePriority
        {
            get { return mediaIconSourcePriority; }
            set
            {
                SetValue(ref mediaIconSourcePriority, value);
                OnPropertyChanged("MediaIconSourcePrioritySummary");
            }
        }

        public string MediaBackgroundSourcePriority
        {
            get { return mediaBackgroundSourcePriority; }
            set
            {
                SetValue(ref mediaBackgroundSourcePriority, value);
                OnPropertyChanged("MediaBackgroundSourcePrioritySummary");
            }
        }

        public string MediaAutomaticPriority { get { return mediaAutomaticPriority; } set { SetValue(ref mediaAutomaticPriority, value); } }
        public string CoverCropAnchor { get { return coverCropAnchor; } set { SetValue(ref coverCropAnchor, value); } }
        public string BackgroundCropAnchor { get { return backgroundCropAnchor; } set { SetValue(ref backgroundCropAnchor, value); } }
        public string ProcessedImageQuality { get { return processedImageQuality; } set { SetValue(ref processedImageQuality, value); } }
        public bool MediaRepairOnlyWhenBetter { get { return mediaRepairOnlyWhenBetter; } set { SetValue(ref mediaRepairOnlyWhenBetter, value); } }
        public bool MediaMinimumQualityEnabled { get { return mediaMinimumQualityEnabled; } set { SetValue(ref mediaMinimumQualityEnabled, value); } }
        public int MediaMinimumCoverWidth { get { return mediaMinimumCoverWidth; } set { SetValue(ref mediaMinimumCoverWidth, Math.Max(64, value)); } }
        public int MediaMinimumIconWidth { get { return mediaMinimumIconWidth; } set { SetValue(ref mediaMinimumIconWidth, Math.Max(32, value)); } }
        public int MediaMinimumBackgroundWidth { get { return mediaMinimumBackgroundWidth; } set { SetValue(ref mediaMinimumBackgroundWidth, Math.Max(320, value)); } }
        public bool EnableExtraMetadataLoaderLogos { get { return enableExtraMetadataLoaderLogos; } set { SetValue(ref enableExtraMetadataLoaderLogos, value); } }

        [DontSerialize]
        public string MediaCoverSourcePrioritySummary { get { return BuildSourcePrioritySummary(MediaCoverSourcePriority, DefaultCoverSourcePriority); } }

        [DontSerialize]
        public string MediaIconSourcePrioritySummary { get { return BuildSourcePrioritySummary(MediaIconSourcePriority, DefaultIconSourcePriority); } }

        [DontSerialize]
        public string MediaBackgroundSourcePrioritySummary { get { return BuildSourcePrioritySummary(MediaBackgroundSourcePriority, DefaultBackgroundSourcePriority); } }

        public MetaDataIASettings()
        {
            ResetTemplates();
        }

        public void EnsureDefaults()
        {
            EnsureDefaults(true);
        }

        public void EnsureDefaults(bool existingSettings)
        {
            if (string.IsNullOrWhiteSpace(ProviderPreset))
            {
                ProviderPreset = ProviderGroq;
            }

            EnsureTextLengthDefaults();
            EnsureCompanyLimitDefaults();
            EnsureSafeDefaults();
            EnsureMediaDefaults(existingSettings);

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

        private void EnsureMediaDefaults(bool existingSettings)
        {
            if (AutoImportKnownGameIds == null)
            {
                AutoImportKnownGameIds = new List<Guid>();
            }

            if (DisabledOriginIntegrationIds == null)
            {
                DisabledOriginIntegrationIds = new List<Guid>();
            }

            if (string.IsNullOrWhiteSpace(MediaProvider))
            {
                MediaProvider = MediaProviderSteamGridDb;
            }

            CoverImageApplyMode = EnsureApplyMode(CoverImageApplyMode, ApplyEmptyOnly);
            IconApplyMode = EnsureApplyMode(IconApplyMode, ApplyEmptyOnly);
            BackgroundImageApplyMode = EnsureApplyMode(BackgroundImageApplyMode, ApplyEmptyOnly);
            ReleaseDateApplyMode = EnsureApplyMode(ReleaseDateApplyMode, ApplyEmptyOnly);
            SeriesApplyMode = EnsureApplyMode(SeriesApplyMode, ApplyEmptyOnly);
            CoverImagePreset = EnsureOption(CoverImagePreset, CoverPresetPlayniteDefined);
            IconPreset = EnsureOption(IconPreset, IconPresetOriginal);
            BackgroundImagePreset = EnsureOption(BackgroundImagePreset, BackgroundPresetSteamHero);
            MediaPickerViewMode = string.Equals(MediaPickerViewMode, MediaPickerViewList, StringComparison.OrdinalIgnoreCase)
                ? MediaPickerViewList
                : MediaPickerViewGrid;
            BackgroundLogoPreference = EnsureOption(BackgroundLogoPreference, BackgroundLogoAny);
            MediaAutomaticPriority = EnsureOption(MediaAutomaticPriority, MediaPriorityBalanced);
            CoverCropAnchor = EnsureOption(CoverCropAnchor, CropAnchorCenter);
            BackgroundCropAnchor = EnsureOption(BackgroundCropAnchor, CropAnchorCenter);
            ProcessedImageQuality = EnsureOption(ProcessedImageQuality, ImageQualityBalanced);
            if (MediaSearchMaxResults < 20)
            {
                MediaSearchMaxResults = 50;
            }

            if (string.IsNullOrWhiteSpace(MediaCoverSourcePriority))
            {
                MediaCoverSourcePriority = DefaultCoverSourcePriority;
            }

            if (string.IsNullOrWhiteSpace(MediaIconSourcePriority))
            {
                MediaIconSourcePriority = DefaultIconSourcePriority;
            }

            if (string.IsNullOrWhiteSpace(MediaBackgroundSourcePriority))
            {
                MediaBackgroundSourcePriority = DefaultBackgroundSourcePriority;
            }

            if (!OriginIntegrationPriorityMigrated)
            {
                if (existingSettings)
                {
                    MediaCoverSourcePriority = AppendSourcePriority(MediaCoverSourcePriority, SourceOriginIntegration);
                    MediaIconSourcePriority = AppendSourcePriority(MediaIconSourcePriority, SourceOriginIntegration);
                    MediaBackgroundSourcePriority = AppendSourcePriority(MediaBackgroundSourcePriority, SourceOriginIntegration);
                }

                OriginIntegrationPriorityMigrated = true;
            }
        }

        private static string AppendSourcePriority(string currentValue, string source)
        {
            var items = (currentValue ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (!items.Any(x => string.Equals(x, source, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(source);
            }

            return string.Join(", ", items);
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

        private void EnsureSafeDefaults()
        {
            if (SafeDefaultsMigrated)
            {
                return;
            }

            StrictCompanyAgeRegion = true;
            GenerateAgeRatings = false;
            GenerateRegions = false;
            MaxDevelopers = 1;
            MaxPublishers = 1;
            MaxTags = Math.Min(Math.Max(8, MaxTags), 10);
            MaxFeatures = Math.Min(Math.Max(6, MaxFeatures), 8);
            MaxCategories = Math.Min(Math.Max(4, MaxCategories), 6);
            CategoriesApplyMode = ApplyAppend;
            PreferExistingCategories = true;
            SafeDefaultsMigrated = true;
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

        public Dictionary<string, List<string>> GetVocabularyTerms(string language)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in new[] { "genres", "tags", "features", "categories" })
            {
                result[field] = new List<string>();
            }

            var code = NormalizeLanguageCode(language);
            var all = ParseVocabularyMemory();
            Dictionary<string, List<string>> byField;
            if (all.TryGetValue(code, out byField))
            {
                foreach (var pair in byField)
                {
                    if (!result.ContainsKey(pair.Key))
                    {
                        result[pair.Key] = new List<string>();
                    }

                    result[pair.Key].AddRange(pair.Value);
                }
            }

            Dictionary<string, List<string>> shared;
            if (all.TryGetValue("*", out shared))
            {
                foreach (var pair in shared)
                {
                    if (!result.ContainsKey(pair.Key))
                    {
                        result[pair.Key] = new List<string>();
                    }

                    result[pair.Key].AddRange(pair.Value);
                }
            }

            return result.ToDictionary(
                x => x.Key,
                x => x.Value
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(200)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        public void LearnVocabulary(string language, AiMetadataResult result)
        {
            if (result == null)
            {
                return;
            }

            var code = NormalizeLanguageCode(language);
            var all = ParseVocabularyMemory();
            Dictionary<string, List<string>> byField;
            if (!all.TryGetValue(code, out byField))
            {
                byField = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                all[code] = byField;
            }

            AddVocabularyTerms(byField, "genres", result.Genres, 120);
            AddVocabularyTerms(byField, "tags", result.Tags, 200);
            AddVocabularyTerms(byField, "features", result.Features, 200);
            AddVocabularyTerms(byField, "categories", result.Categories, 120);
            VocabularyMemory = FormatVocabularyMemory(all);
        }

        private Dictionary<string, Dictionary<string, List<string>>> ParseVocabularyMemory()
        {
            var result = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(VocabularyMemory))
            {
                return result;
            }

            var lines = VocabularyMemory
                .Replace("\r", string.Empty)
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("#", StringComparison.Ordinal))
                .ToList();

            foreach (var line in lines)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                var keyParts = key.Split('.');
                var language = keyParts.Length > 1 ? NormalizeLanguageCode(keyParts[0]) : "*";
                var field = keyParts.Length > 1 ? keyParts[1].Trim() : keyParts[0].Trim();

                if (!IsVocabularyField(field))
                {
                    continue;
                }

                Dictionary<string, List<string>> byField;
                if (!result.TryGetValue(language, out byField))
                {
                    byField = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    result[language] = byField;
                }

                if (!byField.ContainsKey(field))
                {
                    byField[field] = new List<string>();
                }

                byField[field].AddRange(SplitVocabularyValues(value));
            }

            return result;
        }

        private static string FormatVocabularyMemory(Dictionary<string, Dictionary<string, List<string>>> values)
        {
            var lines = new List<string>();
            foreach (var language in values.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var byField = values[language];
                foreach (var field in new[] { "genres", "tags", "features", "categories" })
                {
                    List<string> terms;
                    if (!byField.TryGetValue(field, out terms))
                    {
                        continue;
                    }

                    var clean = terms
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                        .Take(200)
                        .ToList();

                    if (clean.Count > 0)
                    {
                        lines.Add(language + "." + field + "=" + string.Join("; ", clean));
                    }
                }
            }

            return string.Join("\n", lines);
        }

        private static void AddVocabularyTerms(Dictionary<string, List<string>> byField, string field, IEnumerable<string> terms, int maxItems)
        {
            if (!byField.ContainsKey(field))
            {
                byField[field] = new List<string>();
            }

            byField[field].AddRange((terms ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()));

            byField[field] = byField[field]
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxItems)
                .ToList();
        }

        private static IEnumerable<string> SplitVocabularyValues(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        private static bool IsVocabularyField(string field)
        {
            return string.Equals(field, "genres", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "tags", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "features", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field, "categories", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLanguageCode(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? "es" : language.Trim().ToLowerInvariant();
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
                return LocalizeDefaultTemplate(profile.Template);
            }

            return LocalizeDefaultTemplate(string.IsNullOrWhiteSpace(DescriptionTemplate) ? DefaultMediumTemplate : DescriptionTemplate);
        }

        private string LocalizeDefaultTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return template;
            }

            if (string.Equals(template, DefaultShortTemplate, StringComparison.Ordinal))
            {
                return "<p>{short}</p>\n\n<h3>" + Header("features") + "</h3>\n{features}";
            }

            if (string.Equals(template, DefaultMediumTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("brief") + "</h3>\n<p>{short}</p>\n\n<h3>" + Header("synopsis") + "</h3>\n<p>{synopsis}</p>\n\n<h3>" + Header("features") + "</h3>\n{features}\n\n<h3>" + Header("playModes") + "</h3>\n<p>{playModes}</p>\n\n<h3>" + Header("estimatedLength") + "</h3>\n<p>{estimatedLength}</p>\n\n<h3>" + Header("recommendedFor") + "</h3>\n<p>{recommendedFor}</p>";
            }

            if (string.Equals(template, DefaultLongTemplate, StringComparison.Ordinal) ||
                string.Equals(template, LegacyDefaultLongTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("brief") + "</h3>\n<p>{short}</p>\n\n<h3>" + Header("premise") + "</h3>\n<p>{premise}</p>\n\n<h3>" + Header("synopsis") + "</h3>\n<p>{synopsis}</p>\n\n<h3>" + Header("gameplay") + "</h3>\n<p>{gameplay}</p>\n\n<h3>" + Header("toneSetting") + "</h3>\n<p>{tone}</p>\n<p>{setting}</p>\n\n<h3>" + Header("perspectiveModes") + "</h3>\n<p>{perspective}</p>\n<p>{playModes}</p>\n\n<h3>" + Header("features") + "</h3>\n{features}\n\n<h3>" + Header("estimatedLength") + "</h3>\n<p>{estimatedLength}</p>\n\n<h3>" + Header("recommendedFor") + "</h3>\n<p>{recommendedFor}</p>\n\n<h3>" + Header("notes") + "</h3>\n<p>{notes}</p>";
            }

            if (string.Equals(template, DefaultRpgTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("synopsis") + "</h3>\n<p>{synopsis}</p>\n\n<h3>" + Header("roleProgression") + "</h3>\n<p>{gameplay}</p>\n\n<h3>" + Header("worldTone") + "</h3>\n<p>{setting}</p>\n<p>{tone}</p>\n\n<h3>" + Header("rpgFeatures") + "</h3>\n{features}\n\n<h3>" + Header("recommendedFor") + "</h3>\n<p>{recommendedFor}</p>";
            }

            if (string.Equals(template, DefaultAdventureTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("premise") + "</h3>\n<p>{premise}</p>\n\n<h3>" + Header("adventure") + "</h3>\n<p>{synopsis}</p>\n\n<h3>" + Header("explorationPacing") + "</h3>\n<p>{gameplay}</p>\n\n<h3>" + Header("features") + "</h3>\n{features}\n\n<h3>" + Header("idealFor") + "</h3>\n<p>{recommendedFor}</p>";
            }

            if (string.Equals(template, DefaultIndieTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("summary") + "</h3>\n<p>{short}</p>\n\n<h3>" + Header("concept") + "</h3>\n<p>{premise}</p>\n\n<h3>" + Header("style") + "</h3>\n<p>{tone}</p>\n\n<h3>" + Header("keyElements") + "</h3>\n{features}\n\n<h3>" + Header("forPlayers") + "</h3>\n<p>{recommendedFor}</p>";
            }

            if (string.Equals(template, DefaultEmulationTemplate, StringComparison.Ordinal))
            {
                return "<h3>" + Header("summary") + "</h3>\n<p>{short}</p>\n\n<h3>" + Header("context") + "</h3>\n<p>{synopsis}</p>\n\n<h3>" + Header("gameplay") + "</h3>\n<p>{gameplay}</p>\n\n<h3>" + Header("usefulDetails") + "</h3>\n<p>" + Header("platformPerspective") + ": {perspective}</p>\n<p>" + Header("modes") + ": {playModes}</p>\n\n<h3>" + Header("features") + "</h3>\n{features}";
            }

            return template;
        }

        private string Header(string key)
        {
            var language = (Language ?? "es").Trim().ToLowerInvariant();
            var table = HeaderTranslations(language);
            return table.ContainsKey(key) ? table[key] : HeaderTranslations("en")[key];
        }

        private static Dictionary<string, string> HeaderTranslations(string language)
        {
            if (language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Descripcion breve", "Sinopsis", "Caracteristicas principales", "Modos de juego", "Duracion estimada", "Recomendado para", "Premisa", "Jugabilidad", "Tono y ambientacion", "Perspectiva y modos", "Notas", "Rol y progresion", "Mundo y tono", "Caracteristicas RPG", "Aventura", "Exploracion y ritmo", "Ideal para", "Resumen", "Propuesta", "Estilo", "Elementos clave", "Para quien es", "Contexto", "Datos utiles", "Plataforma/perspectiva", "Modos");
            }
            if (language.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Description breve", "Synopsis", "Caracteristiques principales", "Modes de jeu", "Duree estimee", "Recommande pour", "Premisse", "Gameplay", "Ton et univers", "Perspective et modes", "Notes", "Role et progression", "Monde et ton", "Caracteristiques RPG", "Aventure", "Exploration et rythme", "Ideal pour", "Resume", "Concept", "Style", "Elements cles", "Pour les joueurs qui aiment", "Contexte", "Details utiles", "Plateforme/perspective", "Modes");
            }
            if (language.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Kurzbeschreibung", "Synopsis", "Hauptmerkmale", "Spielmodi", "Geschatzte Spielzeit", "Empfohlen fur", "Pramisse", "Gameplay", "Ton und Schauplatz", "Perspektive und Modi", "Notizen", "Rollenspiel und Fortschritt", "Welt und Ton", "RPG-Merkmale", "Abenteuer", "Erkundung und Tempo", "Ideal fur", "Zusammenfassung", "Konzept", "Stil", "Kernelemente", "Fur Spieler, die mogen", "Kontext", "Nutzliche Details", "Plattform/Perspektive", "Modi");
            }
            if (language.StartsWith("it", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Descrizione breve", "Sinossi", "Caratteristiche principali", "Modalita di gioco", "Durata stimata", "Consigliato per", "Premessa", "Gameplay", "Tono e ambientazione", "Prospettiva e modalita", "Note", "Ruolo e progressione", "Mondo e tono", "Caratteristiche RPG", "Avventura", "Esplorazione e ritmo", "Ideale per", "Riepilogo", "Concept", "Stile", "Elementi chiave", "Per giocatori che amano", "Contesto", "Dettagli utili", "Piattaforma/prospettiva", "Modalita");
            }
            if (language.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Descricao breve", "Sinopse", "Caracteristicas principais", "Modos de jogo", "Duracao estimada", "Recomendado para", "Premissa", "Jogabilidade", "Tom e ambientacao", "Perspectiva e modos", "Notas", "RPG e progressao", "Mundo e tom", "Caracteristicas de RPG", "Aventura", "Exploracao e ritmo", "Ideal para", "Resumo", "Proposta", "Estilo", "Elementos-chave", "Para quem gosta de", "Contexto", "Detalhes uteis", "Plataforma/perspectiva", "Modos");
            }
            if (language.StartsWith("pl", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Krotki opis", "Streszczenie", "Glowne cechy", "Tryby gry", "Szacowany czas", "Polecane dla", "Zalozenie", "Rozgrywka", "Ton i swiat", "Perspektywa i tryby", "Notatki", "RPG i progresja", "Swiat i ton", "Cechy RPG", "Przygoda", "Eksploracja i tempo", "Idealne dla", "Podsumowanie", "Koncepcja", "Styl", "Kluczowe elementy", "Dla graczy lubiacych", "Kontekst", "Przydatne szczegoly", "Platforma/perspektywa", "Tryby");
            }
            if (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("Краткое описание", "Синопсис", "Основные особенности", "Режимы игры", "Примерная длительность", "Рекомендуется для", "Завязка", "Геймплей", "Тон и сеттинг", "Перспектива и режимы", "Заметки", "Роль и прогрессия", "Мир и тон", "Особенности RPG", "Приключение", "Исследование и темп", "Идеально для", "Кратко", "Концепция", "Стиль", "Ключевые элементы", "Для игроков, которым нравится", "Контекст", "Полезные детали", "Платформа/перспектива", "Режимы");
            }
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("短い説明", "概要", "主な特徴", "プレイモード", "推定プレイ時間", "おすすめのプレイヤー", "前提", "ゲームプレイ", "雰囲気と舞台", "視点とモード", "メモ", "ロールプレイと成長", "世界観と雰囲気", "RPG要素", "アドベンチャー", "探索とテンポ", "向いている人", "要約", "コンセプト", "スタイル", "重要要素", "おすすめ対象", "背景", "便利な情報", "プラットフォーム/視点", "モード");
            }
            if (language.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("짧은 설명", "시놉시스", "주요 특징", "플레이 모드", "예상 플레이 시간", "추천 대상", "전제", "게임플레이", "분위기와 배경", "시점과 모드", "메모", "역할과 성장", "세계와 분위기", "RPG 특징", "어드벤처", "탐험과 흐름", "적합한 대상", "요약", "콘셉트", "스타일", "핵심 요소", "이런 플레이어에게 추천", "맥락", "유용한 정보", "플랫폼/시점", "모드");
            }
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Headers("简短描述", "剧情简介", "主要特色", "游戏模式", "预计时长", "推荐给", "设定前提", "玩法", "氛围与背景", "视角与模式", "备注", "角色扮演与成长", "世界与氛围", "RPG 特色", "冒险", "探索与节奏", "适合", "概要", "概念", "风格", "关键元素", "适合的玩家", "背景信息", "实用信息", "平台/视角", "模式");
            }

            return Headers("Brief description", "Synopsis", "Main features", "Play modes", "Estimated length", "Recommended for", "Premise", "Gameplay", "Tone and setting", "Perspective and modes", "Notes", "Role-playing and progression", "World and tone", "RPG features", "Adventure", "Exploration and pacing", "Ideal for", "Summary", "Concept", "Style", "Key elements", "For players who like", "Context", "Useful details", "Platform/perspective", "Modes");
        }

        private static Dictionary<string, string> Headers(string brief, string synopsis, string features, string playModes, string estimatedLength, string recommendedFor, string premise, string gameplay, string toneSetting, string perspectiveModes, string notes, string roleProgression, string worldTone, string rpgFeatures, string adventure, string explorationPacing, string idealFor, string summary, string concept, string style, string keyElements, string forPlayers, string context, string usefulDetails, string platformPerspective, string modes)
        {
            return new Dictionary<string, string>
            {
                { "brief", brief },
                { "synopsis", synopsis },
                { "features", features },
                { "playModes", playModes },
                { "estimatedLength", estimatedLength },
                { "recommendedFor", recommendedFor },
                { "premise", premise },
                { "gameplay", gameplay },
                { "toneSetting", toneSetting },
                { "perspectiveModes", perspectiveModes },
                { "notes", notes },
                { "roleProgression", roleProgression },
                { "worldTone", worldTone },
                { "rpgFeatures", rpgFeatures },
                { "adventure", adventure },
                { "explorationPacing", explorationPacing },
                { "idealFor", idealFor },
                { "summary", summary },
                { "concept", concept },
                { "style", style },
                { "keyElements", keyElements },
                { "forPlayers", forPlayers },
                { "context", context },
                { "usefulDetails", usefulDetails },
                { "platformPerspective", platformPerspective },
                { "modes", modes }
            };
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

        public List<string> GetMediaExcludedSearchTerms(MediaKind kind)
        {
            if (kind == MediaKind.Cover)
            {
                return SplitTerms(MediaCoverExcludedSearchTerms);
            }

            if (kind == MediaKind.Icon)
            {
                return SplitTerms(MediaIconExcludedSearchTerms);
            }

            if (kind == MediaKind.Logo)
            {
                return SplitTerms(MediaLogoExcludedSearchTerms);
            }

            return SplitTerms(MediaBackgroundExcludedSearchTerms);
        }

        public void ApplyProviderPreset()
        {
            if (ProviderPreset == ProviderOpenAI)
            {
                Endpoint = "https://api.openai.com/v1/chat/completions";
                Model = "gpt-4.1-mini";
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
                Model = "gemini-3.5-flash-lite";
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
            else if (ProviderPreset == ProviderOpenRouterFree)
            {
                Endpoint = "https://openrouter.ai/api/v1/chat/completions";
                Model = "openrouter/free";
            }
            else if (ProviderPreset == ProviderGroq)
            {
                Endpoint = "https://api.groq.com/openai/v1/chat/completions";
                Model = "llama-3.1-8b-instant";
            }
            else if (ProviderPreset == ProviderCerebras)
            {
                Endpoint = "https://api.cerebras.ai/v1/chat/completions";
                Model = "gpt-oss-120b";
            }
            else if (ProviderPreset == ProviderMistral)
            {
                Endpoint = "https://api.mistral.ai/v1/chat/completions";
                Model = "mistral-small-latest";
            }
        }

        public void RestoreProviderEndpoint()
        {
            if (ProviderPreset == ProviderOpenAI)
            {
                Endpoint = "https://api.openai.com/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderLmStudio)
            {
                Endpoint = "http://localhost:1234/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderOllama)
            {
                Endpoint = "http://localhost:11434/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderGemini)
            {
                Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
            }
            else if (ProviderPreset == ProviderClaude)
            {
                Endpoint = "https://api.anthropic.com/v1/messages";
            }
            else if (ProviderPreset == ProviderOpenRouter)
            {
                Endpoint = "https://openrouter.ai/api/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderOpenRouterFree)
            {
                Endpoint = "https://openrouter.ai/api/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderGroq)
            {
                Endpoint = "https://api.groq.com/openai/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderCerebras)
            {
                Endpoint = "https://api.cerebras.ai/v1/chat/completions";
            }
            else if (ProviderPreset == ProviderMistral)
            {
                Endpoint = "https://api.mistral.ai/v1/chat/completions";
            }
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
        public List<LocalizedOption> ApplyModeOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(ApplySkip, "MTDA_OptionApplySkip", "Do not touch"),
                    Option(ApplyEmptyOnly, "MTDA_OptionApplyEmptyOnly", "Only if empty"),
                    Option(ApplyAppend, "MTDA_OptionApplyAppend", "Append without deleting"),
                    Option(ApplyOverwrite, "MTDA_OptionApplyOverwrite", "Overwrite")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> ProviderPresetOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(ProviderOpenRouterFree, "MTDA_ProviderOpenRouterFree", "OpenRouter Free (limited)"),
                    Option(ProviderGroq, "MTDA_ProviderGroqFree", "Groq (free tier)"),
                    Option(ProviderGemini, "MTDA_ProviderGeminiFree", "Google Gemini (free tier)"),
                    Option(ProviderCerebras, "MTDA_ProviderCerebras", "Cerebras (free tier)"),
                    Option(ProviderMistral, "MTDA_ProviderMistral", "Mistral AI (free mode)"),
                    Option(ProviderLmStudio, "MTDA_ProviderLmStudio", "LM Studio local"),
                    Option(ProviderOllama, "MTDA_ProviderOllama", "Ollama local"),
                    Option(ProviderOpenAI, "MTDA_ProviderOpenAI", "OpenAI"),
                    Option(ProviderClaude, "MTDA_ProviderClaude", "Claude Anthropic"),
                    Option(ProviderOpenRouter, "MTDA_ProviderOpenRouter", "OpenRouter"),
                    Option(ProviderCustom, "MTDA_ProviderCustom", "Custom OpenAI-compatible")
                };
            }
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
                    new LanguageOption("fr", "Francais (fr)"),
                    new LanguageOption("de", "Deutsch (de)"),
                    new LanguageOption("it", "Italiano (it)"),
                    new LanguageOption("pt", "Portugues (pt)"),
                    new LanguageOption("pt-BR", "Portugues do Brasil (pt-BR)"),
                    new LanguageOption("pl", "Polski (pl)"),
                    new LanguageOption("nl", "Nederlands (nl)"),
                    new LanguageOption("sv", "Svenska (sv)"),
                    new LanguageOption("no", "Norsk (no)"),
                    new LanguageOption("da", "Dansk (da)"),
                    new LanguageOption("fi", "Suomi (fi)"),
                    new LanguageOption("tr", "Turkce (tr)"),
                    new LanguageOption("cs", "Cestina (cs)"),
                    new LanguageOption("hu", "Magyar (hu)"),
                    new LanguageOption("ro", "Romana (ro)"),
                    new LanguageOption("sk", "Slovencina (sk)"),
                    new LanguageOption("sl", "Slovenscina (sl)"),
                    new LanguageOption("hr", "Hrvatski (hr)"),
                    new LanguageOption("sr", "Srpski (sr)"),
                    new LanguageOption("bg", "Bulgarian (bg)"),
                    new LanguageOption("el", "Greek (el)"),
                    new LanguageOption("ca", "Catala (ca)"),
                    new LanguageOption("gl", "Galego (gl)"),
                    new LanguageOption("eu", "Euskara (eu)"),
                    new LanguageOption("et", "Eesti (et)"),
                    new LanguageOption("lv", "Latviesu (lv)"),
                    new LanguageOption("lt", "Lietuviu (lt)"),
                    new LanguageOption("ru", "Russian (ru)"),
                    new LanguageOption("uk", "Ukrainian (uk)"),
                    new LanguageOption("ar", "Arabic (ar)"),
                    new LanguageOption("he", "Hebrew (he)"),
                    new LanguageOption("hi", "Hindi (hi)"),
                    new LanguageOption("id", "Bahasa Indonesia (id)"),
                    new LanguageOption("ms", "Bahasa Melayu (ms)"),
                    new LanguageOption("th", "Thai (th)"),
                    new LanguageOption("vi", "Vietnamese (vi)"),
                    new LanguageOption("ja", "Japanese (ja)"),
                    new LanguageOption("ko", "Korean (ko)"),
                    new LanguageOption("zh", "Chinese (zh)"),
                    new LanguageOption("zh-CN", "Simplified Chinese (zh-CN)"),
                    new LanguageOption("zh-TW", "Traditional Chinese (zh-TW)")
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
                    return Loc("MTDA_ProviderHelpOpenAI", "OpenAI: create the key at platform.openai.com/api-keys. ChatGPT Plus/Pro does not include API usage; the API uses separate billing in the OpenAI Platform.");
                }

                if (ProviderPreset == ProviderGemini)
                {
                    return Loc("MTDA_ProviderHelpGemini", "Google Gemini: create the key in Google AI Studio. Gemini Pro/Google AI Pro in the app does not automatically increase API quota or availability; the API has its own tiers and limits.");
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return Loc("MTDA_ProviderHelpClaude", "Claude Anthropic: create the key in Anthropic Console. The claude.ai subscription and the API are separate products.");
                }

                if (ProviderPreset == ProviderOpenRouter)
                {
                    return Loc("MTDA_ProviderHelpOpenRouter", "OpenRouter: create the key in OpenRouter. You can choose free models if the model ends in :free or appears as Free, but they have limits and variable availability.");
                }

                if (ProviderPreset == ProviderOpenRouterFree)
                {
                    return Loc("MTDA_ProviderHelpOpenRouterFree", "OpenRouter Free: create a free API key and use the automatic openrouter/free router. It chooses an available free model for each request, so speed and output consistency can vary.");
                }

                if (ProviderPreset == ProviderGroq)
                {
                    return Loc("MTDA_ProviderHelpGroq", "Groq: create the key in GroqCloud Console. It usually offers a free start with usage limits.");
                }

                if (ProviderPreset == ProviderCerebras)
                {
                    return Loc("MTDA_ProviderHelpCerebras", "Cerebras: create a free API key in Cerebras Cloud. The free tier has lower rate limits but provides very fast inference and does not require a subscription.");
                }

                if (ProviderPreset == ProviderMistral)
                {
                    return Loc("MTDA_ProviderHelpMistral", "Mistral AI: create an API key in Mistral Studio. Free mode is enabled by default without a credit card, with usage and rate limits.");
                }

                if (ProviderPreset == ProviderLmStudio)
                {
                    return Loc("MTDA_ProviderHelpLmStudio", "LM Studio local: no API key is needed. Open LM Studio, load a model, and enable the local server in the Developer tab.");
                }

                if (ProviderPreset == ProviderOllama)
                {
                    return Loc("MTDA_ProviderHelpOllama", "Ollama local: no API key is needed. Install Ollama, download a model with 'ollama pull', and keep the local service running.");
                }

                return Loc("MTDA_ProviderHelpCustom", "Custom provider: use the URL, model, and API key specified by that OpenAI-compatible provider.");
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

                if (ProviderPreset == ProviderOpenRouter || ProviderPreset == ProviderOpenRouterFree)
                {
                    return "https://openrouter.ai/settings/keys";
                }

                if (ProviderPreset == ProviderGroq)
                {
                    return "https://console.groq.com/keys";
                }

                if (ProviderPreset == ProviderCerebras)
                {
                    return "https://cloud.cerebras.ai/";
                }

                if (ProviderPreset == ProviderMistral)
                {
                    return "https://console.mistral.ai/";
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
        public string ProviderUsageUrl
        {
            get
            {
                if (ProviderPreset == ProviderOpenAI)
                {
                    return "https://platform.openai.com/usage";
                }

                if (ProviderPreset == ProviderGemini)
                {
                    return "https://aistudio.google.com/usage";
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return "https://console.anthropic.com/settings/limits";
                }

                if (ProviderPreset == ProviderOpenRouter || ProviderPreset == ProviderOpenRouterFree)
                {
                    return "https://openrouter.ai/activity";
                }

                if (ProviderPreset == ProviderGroq)
                {
                    return "https://console.groq.com/settings/limits";
                }

                if (ProviderPreset == ProviderCerebras)
                {
                    return "https://cloud.cerebras.ai/";
                }

                if (ProviderPreset == ProviderMistral)
                {
                    return "https://console.mistral.ai/";
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
                    return Loc("MTDA_ProviderBillingOpenAI", "Signing in with ChatGPT Plus is not enough for this plugin: you need an OpenAI Platform API key and API billing/credit. Plus only gives benefits inside ChatGPT.");
                }

                if (ProviderPreset == ProviderLmStudio || ProviderPreset == ProviderOllama)
                {
                    return Loc("MTDA_ProviderBillingLocal", "Recommended option if you do not want to pay: the cost is your own hardware. Speed and quality depend on the model and your PC.");
                }

                if (ProviderPreset == ProviderGemini ||
                    ProviderPreset == ProviderGroq ||
                    ProviderPreset == ProviderOpenRouter ||
                    ProviderPreset == ProviderOpenRouterFree ||
                    ProviderPreset == ProviderCerebras ||
                    ProviderPreset == ProviderMistral)
                {
                    return Loc("MTDA_ProviderBillingFreeQuota", "It may work without paying if you choose a free model/quota, but if you hit limits or high demand you will need to wait, switch to a more available model, or enable billing depending on the provider.");
                }

                if (ProviderPreset == ProviderClaude)
                {
                    return Loc("MTDA_ProviderBillingClaude", "Claude API usually requires its own billing; having Claude Pro on the web does not mean you have API credit.");
                }

                return Loc("MTDA_ProviderBillingCustom", "Check the provider terms before processing large libraries.");
            }
        }

        [DontSerialize]
        public List<LocalizedOption> ToneOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option("Neutral", "MTDA_OptionToneNeutral", "Neutral"),
                    Option("Enciclopedico", "MTDA_OptionToneEncyclopedic", "Encyclopedic"),
                    Option("Tienda", "MTDA_OptionToneStore", "Store"),
                    Option("Critico", "MTDA_OptionToneCritical", "Critical"),
                    Option("Breve", "MTDA_OptionToneBrief", "Brief"),
                    Option("Gamer", "MTDA_OptionToneGamer", "Gamer"),
                    Option("Entusiasta", "MTDA_OptionToneEnthusiastic", "Enthusiastic"),
                    Option("Retro", "MTDA_OptionToneRetro", "Retro"),
                    Option("Tecnico", "MTDA_OptionToneTechnical", "Technical"),
                    Option("Familiar", "MTDA_OptionToneFamily", "Family-friendly")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> LengthOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option("Corta", "MTDA_OptionLengthShort", "Short"),
                    Option("Media", "MTDA_OptionLengthMedium", "Medium"),
                    Option("Larga", "MTDA_OptionLengthLong", "Long"),
                    Option("Extra larga", "MTDA_OptionLengthExtraLong", "Extra long")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> ExistingMetadataModeOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option("Usar como contexto", "MTDA_OptionMetadataContext", "Use as context"),
                    Option("Normalizar", "MTDA_OptionMetadataNormalize", "Normalize"),
                    Option("Ignorar", "MTDA_OptionMetadataIgnore", "Ignore")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> MediaProviderOptions
        {
            get { return new List<LocalizedOption> { Option("Varias fuentes", "MTDA_OptionMediaMultipleSources", "Multiple sources"), Option(MediaProviderSteamGridDb, "MTDA_OptionMediaSteamGridDb", "SteamGridDB") }; }
        }

        [DontSerialize]
        public List<LocalizedOption> CoverImagePresetOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(CoverPresetPlayniteDefined, "MTDA_OptionCoverPlayniteDefined", "Defined by Playnite"),
                    Option(CoverPresetPlayniteVertical, "MTDA_OptionCoverPlayniteVertical", "Playnite vertical (600x900)"),
                    Option(CoverPresetOriginal, "MTDA_OptionOriginal", "Original"),
                    Option(CoverPresetSquare, "MTDA_OptionCoverSquare", "Playnite square (600x600)"),
                    Option(CoverPresetHorizontal, "MTDA_OptionCoverHorizontal", "Horizontal/banner (920x430)")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> IconPresetOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(IconPresetOriginal, "MTDA_OptionIconOriginal", "Original"),
                    Option(IconPresetSquare, "MTDA_OptionIconSquare", "Square 256"),
                    Option(IconPresetRounded, "MTDA_OptionIconRounded", "Rounded 256"),
                    Option(IconPresetCircle, "MTDA_OptionIconCircle", "Circle 256")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> BackgroundImagePresetOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(BackgroundPresetSteamHero, "MTDA_OptionBackgroundSteamHero", "Steam hero (3840x1240)"),
                    Option(BackgroundPresetSteamHeroSmall, "MTDA_OptionBackgroundSteamHeroSmall", "Light Steam hero (1920x620)"),
                    Option(BackgroundPresetHd, "MTDA_OptionBackgroundHd", "Fullscreen HD (1280x720)"),
                    Option(BackgroundPresetFullHd, "MTDA_OptionBackgroundFullHd", "Fullscreen Full HD (1920x1080)"),
                    Option(BackgroundPresetQhd, "MTDA_OptionBackgroundQhd", "Fullscreen QHD (2560x1440)"),
                    Option(BackgroundPreset4K, "MTDA_OptionBackground4K", "Fullscreen 4K (3840x2160)"),
                    Option(BackgroundPresetOriginal, "MTDA_OptionOriginal", "Original")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> BackgroundLogoPreferenceOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(BackgroundLogoAny, "MTDA_OptionBackgroundLogoAny", "Any"),
                    Option(BackgroundLogoPreferNoLogo, "MTDA_OptionBackgroundLogoNoLogo", "Prefer without logo"),
                    Option(BackgroundLogoPreferLogo, "MTDA_OptionBackgroundLogoWithLogo", "Prefer with logo")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> MediaAutomaticPriorityOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(MediaPriorityBalanced, "MTDA_OptionMediaPriorityBalanced", "Balanced"),
                    Option(MediaPrioritySourceFirst, "MTDA_OptionMediaPrioritySourceFirst", "Source first"),
                    Option(MediaPriorityResolutionFirst, "MTDA_OptionMediaPriorityResolutionFirst", "Resolution first"),
                    Option(MediaPriorityStrictQuality, "MTDA_OptionMediaPriorityStrictQuality", "Strict quality")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> CropAnchorOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(CropAnchorCenter, "MTDA_OptionCropCenter", "Center"),
                    Option(CropAnchorTop, "MTDA_OptionCropTop", "Top"),
                    Option(CropAnchorBottom, "MTDA_OptionCropBottom", "Bottom"),
                    Option(CropAnchorLeft, "MTDA_OptionCropLeft", "Left"),
                    Option(CropAnchorRight, "MTDA_OptionCropRight", "Right"),
                    Option(CropAnchorTopLeft, "MTDA_OptionCropTopLeft", "Top left"),
                    Option(CropAnchorTopRight, "MTDA_OptionCropTopRight", "Top right"),
                    Option(CropAnchorBottomLeft, "MTDA_OptionCropBottomLeft", "Bottom left"),
                    Option(CropAnchorBottomRight, "MTDA_OptionCropBottomRight", "Bottom right")
                };
            }
        }

        [DontSerialize]
        public List<LocalizedOption> ProcessedImageQualityOptions
        {
            get
            {
                return new List<LocalizedOption>
                {
                    Option(ImageQualitySpaceSaving, "MTDA_OptionImageQualitySpaceSaving", "Space saving"),
                    Option(ImageQualityBalanced, "MTDA_OptionImageQualityBalanced", "Balanced"),
                    Option(ImageQualityHigh, "MTDA_OptionImageQualityHigh", "High"),
                    Option(ImageQualityMaximum, "MTDA_OptionImageQualityMaximum", "Maximum")
                };
            }
        }

        [DontSerialize]
        public string SteamGridDbHelp
        {
            get { return Loc("MTDA_MediaHelp", "The plugin combines media sources: official Steam, PlayStation Store, Xbox Store, Epic Store when usable, SteamGridDB as a community source if you configure its API key, plus RAWG.io, Wallhaven, MobyGames and IGDB when enabled. Wallhaven is limited to SFW 16:9 backgrounds. In automatic mode it prioritizes official assets and then applies format, score and filter preferences."); }
        }

        private static LocalizedOption Option(string value, string key, string fallback)
        {
            return new LocalizedOption(value, Loc(key, fallback));
        }

        private static string BuildSourcePrioritySummary(string value, string fallback)
        {
            var summary = string.IsNullOrWhiteSpace(value) ? fallback : value;
            return summary.Replace(SourceOriginIntegration, Loc("MTDA_SourceOriginIntegration", "Origin library integration"));
        }

        private static string Loc(string key, string fallback)
        {
            return PluginLocalization.GetString(key, fallback);
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

    public class OriginLibraryIntegrationOption : ObservableObject
    {
        private bool isEnabled;
        private readonly Action<bool> changed;

        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public bool IsEnabled
        {
            get { return isEnabled; }
            set
            {
                if (isEnabled == value)
                {
                    return;
                }

                SetValue(ref isEnabled, value);
                if (changed != null)
                {
                    changed(value);
                }
            }
        }

        public OriginLibraryIntegrationOption(Guid id, string name, bool enabled, Action<bool> changed)
        {
            Id = id;
            Name = name;
            isEnabled = enabled;
            this.changed = changed;
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
        private ObservableCollection<OriginLibraryIntegrationOption> originLibraryIntegrations = new ObservableCollection<OriginLibraryIntegrationOption>();

        public MetaDataIAPlugin Plugin { get { return plugin; } }
        public ObservableCollection<OriginLibraryIntegrationOption> OriginLibraryIntegrations
        {
            get { return originLibraryIntegrations; }
            private set { SetValue(ref originLibraryIntegrations, value); }
        }

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
                OnPropertyChanged("SelectedTemplateStatusText");
                OnPropertyChanged("CanDeleteSelectedTemplate");
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
                    OnPropertyChanged("SelectedTemplateStatusText");
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
                    OnPropertyChanged("SelectedTemplateStatusText");
                }
            }
        }

        public string SelectedTemplateStatusText
        {
            get
            {
                if (SelectedTemplate == null)
                {
                    return string.Empty;
                }

                var defaultTemplate = MetaDataIASettings.CreateDefaultTemplates()
                    .FirstOrDefault(x => string.Equals(x.Name, SelectedTemplate.Name, StringComparison.OrdinalIgnoreCase));
                if (defaultTemplate == null)
                {
                    return PluginLocalization.GetString("MTDA_TemplateCustomStatus", "Custom");
                }

                return string.Equals(defaultTemplate.Template, SelectedTemplate.Template, StringComparison.Ordinal)
                    ? PluginLocalization.GetString("MTDA_TemplateDefaultStatus", "Default")
                    : PluginLocalization.GetString("MTDA_TemplateModifiedStatus", "Modified");
            }
        }

        public bool CanDeleteSelectedTemplate
        {
            get { return Settings != null && Settings.Templates != null && Settings.Templates.Count > 1 && SelectedTemplate != null; }
        }

        public MetaDataIASettingsViewModel(MetaDataIAPlugin plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<MetaDataIASettings>();
            Settings = savedSettings ?? new MetaDataIASettings();
            Settings.UnprotectSecretsAfterLoad();
            Settings.EnsureDefaults(savedSettings != null);
            if (savedSettings != null && !Settings.SetupWizardMigrationApplied)
            {
                Settings.SetupWizardCompleted = true;
                Settings.SetupWizardMigrationApplied = true;
                plugin.SaveSettingsSecurely(Settings);
            }
            else if (savedSettings == null)
            {
                Settings.SetupWizardMigrationApplied = true;
            }
            RefreshOriginLibraryIntegrations();
            SelectedTemplate = Settings.GetActiveTemplate();
        }

        public bool IsSetupWizardPending
        {
            get { return Settings != null && !Settings.SetupWizardCompleted; }
        }

        public void RefreshOriginLibraryIntegrations()
        {
            var detected = new PlayniteIntegrationService(plugin.Api, Settings).GetDetectedIntegrations();
            var disabled = Settings.DisabledOriginIntegrationIds ?? new List<Guid>();
            OriginLibraryIntegrations = new ObservableCollection<OriginLibraryIntegrationOption>(detected.Select(info =>
                new OriginLibraryIntegrationOption(info.Id, info.Name, !disabled.Contains(info.Id), enabled =>
                {
                    var current = Settings.DisabledOriginIntegrationIds ?? new List<Guid>();
                    if (enabled)
                    {
                        current.RemoveAll(x => x == info.Id);
                    }
                    else if (!current.Contains(info.Id))
                    {
                        current.Add(info.Id);
                    }

                    Settings.DisabledOriginIntegrationIds = current;
                })));
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
            RefreshOriginLibraryIntegrations();
            SelectedTemplate = Settings == null ? null : Settings.GetActiveTemplate();
        }

        public void EndEdit()
        {
            SyncSelectedTemplate();

            plugin.SaveSettingsSecurely(Settings);
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
            var name = CreateUniqueTemplateName(PluginLocalization.GetString("MTDA_NewTemplateName", "New template"));
            var profile = new TemplateProfile(name, MetaDataIASettings.DefaultMediumTemplate);
            Settings.Templates.Add(profile);
            SelectedTemplate = profile;
            OnPropertyChanged("CanDeleteSelectedTemplate");
        }

        public void DuplicateSelectedTemplate()
        {
            Settings.EnsureDefaults();
            if (SelectedTemplate == null)
            {
                return;
            }

            SyncSelectedTemplate();
            var suffix = PluginLocalization.GetString("MTDA_TemplateCopySuffix", "copy");
            var baseName = (SelectedTemplate.Name ?? string.Empty).Trim();
            var name = CreateUniqueTemplateName(string.IsNullOrWhiteSpace(baseName) ? suffix : baseName + " - " + suffix);
            var profile = new TemplateProfile(name, SelectedTemplate.Template ?? string.Empty);
            Settings.Templates.Add(profile);
            SelectedTemplate = profile;
            OnPropertyChanged("CanDeleteSelectedTemplate");
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
            OnPropertyChanged("CanDeleteSelectedTemplate");
        }

        public void RestoreDefaultTemplates()
        {
            Settings.ResetTemplates();
            SelectedTemplate = Settings.GetActiveTemplate();
            OnPropertyChanged("CanDeleteSelectedTemplate");
        }

        private string CreateUniqueTemplateName(string requestedName)
        {
            var baseName = string.IsNullOrWhiteSpace(requestedName)
                ? PluginLocalization.GetString("MTDA_NewTemplateName", "New template")
                : requestedName.Trim();
            var name = baseName;
            var index = 2;
            while (Settings.Templates.Any(x => x != null && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = baseName + " " + index;
                index++;
            }

            return name;
        }

        public void ReplaceSettingsFromBackup(MetaDataIASettings importedSettings)
        {
            if (importedSettings == null)
            {
                return;
            }

            importedSettings.EnsureDefaults();
            Settings = importedSettings;
            RefreshOriginLibraryIntegrations();
            SelectedTemplate = Settings.GetActiveTemplate();
            editingClone = Serialization.GetClone(Settings);
            plugin.SaveSettingsSecurely(Settings);
        }

        public void ReplaceSettingsFromWizard(MetaDataIASettings wizardSettings)
        {
            if (wizardSettings == null)
            {
                return;
            }

            wizardSettings.SetupWizardCompleted = true;
            wizardSettings.SetupWizardMigrationApplied = true;
            wizardSettings.EnsureDefaults();
            Settings = wizardSettings;
            RefreshOriginLibraryIntegrations();
            SelectedTemplate = Settings.GetActiveTemplate();
            editingClone = Serialization.GetClone(Settings);
            plugin.SaveSettingsSecurely(Settings);
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
