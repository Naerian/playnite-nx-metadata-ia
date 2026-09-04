using System;
using System.Collections.Generic;

namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonPropertyAttribute : Attribute
    {
        public JsonPropertyAttribute(string name) { }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }
}

namespace Playnite.SDK
{
    public interface IPlayniteAPI
    {
        IDatabaseAPI Database { get; }
    }

    public interface IDatabaseAPI
    {
        GameCollection Games { get; }
        SeriesCollection Series { get; }
    }

    public sealed class GameCollection
    {
        public IEnumerable<Playnite.SDK.Models.Game> GetClone() { return new List<Playnite.SDK.Models.Game>(); }
    }

    public sealed class SeriesCollection
    {
        public Playnite.SDK.Models.Series Get(Guid id) { return null; }
    }
}

namespace Playnite.SDK.Models
{
    public class DatabaseObject
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class Tag : DatabaseObject { }
    public sealed class Genre : DatabaseObject { }
    public sealed class Feature : DatabaseObject { }
    public sealed class Series : DatabaseObject { }

    public sealed class Game : DatabaseObject
    {
        public List<Guid> SeriesIds { get; set; }
        public List<Series> Series { get; set; }
        public List<Tag> Tags { get; set; }
        public List<Genre> Genres { get; set; }
        public List<Feature> Features { get; set; }
    }
}
