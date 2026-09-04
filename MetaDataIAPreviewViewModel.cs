using Playnite.SDK.Models;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    public class MetaDataIAPreviewViewModel
    {
        public Game Game { get; private set; }
        public AiMetadataResult Result { get; private set; }

        public string Title { get { return Game == null ? "Metadata AI" : Game.Name; } }
        public string Description { get { return Result == null ? string.Empty : Result.Description; } }
        public string Detail
        {
            get
            {
                if (Result == null)
                {
                    return string.Empty;
                }

                var lines = new List<string>();
                Add(lines, "Premisa", Result.Premise);
                Add(lines, "Jugabilidad", Result.Gameplay);
                Add(lines, "Tono", Result.Tone);
                Add(lines, "Ambientacion", Result.Setting);
                Add(lines, "Perspectiva", Result.Perspective);
                Add(lines, "Modos de juego", Result.PlayModes);
                Add(lines, "Duracion estimada", Result.EstimatedLength);
                Add(lines, "Juegos similares", Result.SimilarGames);
                Add(lines, "Notas", Result.Notes);
                return string.Join("\n\n", lines);
            }
        }
        public string Genres { get { return Join(Result == null ? null : Result.Genres); } }
        public string Tags { get { return Join(Result == null ? null : Result.Tags); } }
        public string Features { get { return Join(Result == null ? null : Result.Features); } }
        public string SeriesContext { get { return Result == null || Result.SeriesContextDiagnostics == null ? string.Empty : Result.SeriesContextDiagnostics.ToDisplayText(); } }
        public string Developers { get { return Join(Result == null ? null : Result.Developers); } }
        public string Publishers { get { return Join(Result == null ? null : Result.Publishers); } }
        public string AgeRatings { get { return Join(Result == null ? null : Result.AgeRatings); } }
        public string Regions { get { return Join(Result == null ? null : Result.Regions); } }
        public string Categories { get { return Join(Result == null ? null : Result.Categories); } }

        public MetaDataIAPreviewViewModel(Game game, AiMetadataResult result)
        {
            Game = game;
            Result = result;
        }

        private static string Join(IEnumerable<string> values)
        {
            return values == null ? string.Empty : string.Join(", ", values.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static void Add(List<string> lines, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(label + ": " + value);
            }
        }
    }
}
