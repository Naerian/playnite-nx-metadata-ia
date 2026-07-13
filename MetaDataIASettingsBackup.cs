using System;

namespace MetaDataIAPlugin
{
    public class MetaDataIASettingsBackup
    {
        public string AddonId { get; set; }
        public string PluginName { get; set; }
        public string BackupVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public MetaDataIASettings Settings { get; set; }

        public MetaDataIASettingsBackup()
        {
            AddonId = "MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83";
            PluginName = "Metadata AI";
            BackupVersion = "1.1";
            ExportedAt = DateTime.Now;
        }
    }
}
