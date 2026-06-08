using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    public class MetaDataIAProvider : OnDemandMetadataProvider
    {
        private readonly MetadataRequestOptions options;
        private readonly MetaDataIAPlugin plugin;
        private readonly MetaDataIASettings settings;
        private AiMetadataResult cachedResult;

        public override List<MetadataField> AvailableFields
        {
            get { return plugin.SupportedFields; }
        }

        public MetaDataIAProvider(MetadataRequestOptions options, MetaDataIAPlugin plugin, MetaDataIASettings settings)
        {
            this.options = options;
            this.plugin = plugin;
            this.settings = settings;
        }

        public override string GetDescription(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateDescription || settings.DescriptionApplyMode == MetaDataIASettings.ApplySkip)
            {
                return null;
            }

            return Generate().Description;
        }

        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateGenres || settings.GenresApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Genres.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateDevelopers || settings.DevelopersApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Developers.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
        {
            if (!settings.GeneratePublishers || settings.PublishersApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Publishers.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateTags || settings.TagsApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Tags.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetFeatures(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateFeatures || settings.FeaturesApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Features.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetAgeRatings(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateAgeRatings || settings.AgeRatingsApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().AgeRatings.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<MetadataProperty> GetRegions(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateRegions || settings.RegionsApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<MetadataProperty>();
            }

            return Generate().Regions.Select(x => new MetadataNameProperty(x));
        }

        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            if (!settings.GenerateLinks || settings.LinksApplyMode == MetaDataIASettings.ApplySkip)
            {
                return Enumerable.Empty<Link>();
            }

            return Generate().Links.Select(x => new Link(x.Name, x.Url));
        }

        public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
        {
            if (!settings.DownloadCoverImage || settings.CoverImageApplyMode == MetaDataIASettings.ApplySkip)
            {
                return null;
            }

            return GenerateMedia(MediaKind.Cover);
        }

        public override MetadataFile GetIcon(GetMetadataFieldArgs args)
        {
            if (!settings.DownloadIcon || settings.IconApplyMode == MetaDataIASettings.ApplySkip)
            {
                return null;
            }

            return GenerateMedia(MediaKind.Icon);
        }

        public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
        {
            if (!settings.DownloadBackgroundImage || settings.BackgroundImageApplyMode == MetaDataIASettings.ApplySkip)
            {
                return null;
            }

            return GenerateMedia(MediaKind.Background);
        }

        private AiMetadataResult Generate()
        {
            if (cachedResult != null)
            {
                return cachedResult;
            }

            if (!settings.IsConfigured)
            {
                throw new InvalidOperationException(PluginLocalization.GetString("MTDA_ErrorMetadataProviderNotConfigured", "Metadata AI is not configured. Open the plugin settings and set endpoint, model and API key."));
            }

            cachedResult = new MetadataGenerationService(settings).GenerateAsync(options.GameData).GetAwaiter().GetResult();
            return cachedResult;
        }

        private MetadataFile GenerateMedia(MediaKind kind)
        {
            if (!settings.IsMediaConfigured)
            {
                throw new InvalidOperationException(PluginLocalization.GetString("MTDA_ErrorMediaProviderNotConfigured", "Metadata AI media is not configured. Open the plugin settings and configure at least one usable media source."));
            }

            var media = new MediaGenerationService(settings).GenerateAsync(options.GameData, kind).GetAwaiter().GetResult();
            return MediaGenerationService.ToMetadataFile(media);
        }
    }
}
