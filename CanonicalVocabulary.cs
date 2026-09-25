using System;
using System.Collections.Generic;
using System.Linq;

namespace MetaDataIAPlugin
{
    /// <summary>
    /// Plugin-owned preferred spellings (and aliases) for genres, tags, features and categories.
    /// Playnite's database remains the storage; this vocabulary decides how terms should be written.
    /// </summary>
    public static class CanonicalVocabulary
    {
        public sealed class Term
        {
            public Term(string preferred, params string[] aliases)
            {
                Preferred = preferred ?? string.Empty;
                Aliases = (aliases ?? new string[0])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Where(x => !string.Equals(x, Preferred, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            public string Preferred { get; private set; }

            public IList<string> Aliases { get; private set; }

            public IEnumerable<string> AllNames
            {
                get
                {
                    yield return Preferred;
                    foreach (var alias in Aliases)
                    {
                        yield return alias;
                    }
                }
            }
        }

        public static Dictionary<string, List<string>> GetPreferredNames(string language)
        {
            return GetTerms(language).ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(x => x.Preferred).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        public static Dictionary<string, List<Term>> GetTerms(string language)
        {
            if (!string.IsNullOrWhiteSpace(language) &&
                language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return BuildEnglish();
            }

            if (!string.IsNullOrWhiteSpace(language) &&
                !language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, List<Term>>(StringComparer.OrdinalIgnoreCase);
            }

            return BuildSpanish();
        }

        public static List<Term> GetFieldTerms(string language, string field)
        {
            List<Term> terms;
            if (!GetTerms(language).TryGetValue(field ?? string.Empty, out terms) || terms == null)
            {
                return new List<Term>();
            }

            return terms;
        }

        private static Dictionary<string, List<Term>> BuildEnglish()
        {
            return new Dictionary<string, List<Term>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "genres",
                    new List<Term>
                    {
                        new Term("Action"), new Term("Adventure"), new Term("RPG", "Role-playing", "Role playing"),
                        new Term("Strategy"), new Term("Simulation"), new Term("Sports"), new Term("Racing"),
                        new Term("Fighting"), new Term("Platformer", "Platform"), new Term("Puzzle"),
                        new Term("Shooter", "FPS", "TPS"), new Term("Horror"), new Term("Survival"),
                        new Term("Stealth"), new Term("Roguelike", "Rogue-like", "Roguelite"),
                        new Term("Open world"), new Term("Metroidvania"), new Term("Visual novel"), new Term("Rhythm")
                    }
                },
                {
                    "tags",
                    new List<Term>
                    {
                        new Term("Single-player", "Single player", "Singleplayer"),
                        new Term("Multiplayer", "Multi-player"),
                        new Term("Co-op", "Coop", "Cooperative"),
                        new Term("Online co-op", "Online coop", "Online cooperative"),
                        new Term("Local co-op", "Local coop", "Local cooperative"),
                        new Term("Competitive"), new Term("PvP"), new Term("PvE"),
                        new Term("Social deduction"), new Term("Exploration"), new Term("Building"),
                        new Term("Management"), new Term("Crafting"), new Term("Deep story"),
                        new Term("Narrative"), new Term("Comedy"), new Term("Difficult"),
                        new Term("Casual"), new Term("Retro"), new Term("Anime"), new Term("Pixel art"),
                        new Term("Science fiction", "Sci-fi", "Sci fi"), new Term("Fantasy"),
                        new Term("Cyberpunk"), new Term("Post-apocalyptic", "Post apocalyptic"),
                        new Term("Sandbox"), new Term("Procedural")
                    }
                },
                {
                    "features",
                    new List<Term>
                    {
                        new Term("Single-player", "Single player", "Singleplayer"),
                        new Term("Online multiplayer", "Multiplayer online", "Online Multi-Player"),
                        new Term("Local multiplayer", "Multiplayer local", "Shared/Split Screen Multiplayer", "Local Multi-Player"),
                        new Term("Online co-op", "Online Coop", "Online Co-op", "Co-op Online", "Online Cooperative"),
                        new Term("Local co-op", "Local Coop", "Local Co-op", "Co-op Local", "Local Cooperative"),
                        new Term("Split screen", "Shared/Split Screen", "Shared Split Screen", "Split-Screen"),
                        new Term("Controller support", "Full controller support", "Partial Controller Support"),
                        new Term("Achievements", "Steam Achievements"),
                        new Term("Cloud saves", "Steam Cloud"),
                        new Term("Steam trading cards", "Trading Cards"),
                        new Term("Steam Deck compatibility", "Steam Deck"),
                        new Term("Cross-play", "Crossplay", "Cross-Platform Multiplayer"),
                        new Term("Level editor"),
                        new Term("PvP modes", "PvP"),
                        new Term("PvE modes", "PvE"),
                        new Term("In-app purchases", "In-App Purchases", "IAP")
                    }
                },
                {
                    "categories",
                    new List<Term>
                    {
                        new Term("Favorites"), new Term("Backlog"), new Term("Completed"), new Term("Abandoned"),
                        new Term("Co-op games", "Coop games"), new Term("Quick sessions"), new Term("Long sessions"),
                        new Term("Relaxing"), new Term("Challenges"), new Term("Narrative"),
                        new Term("Multiplayer"), new Term("Indie"), new Term("Retro"), new Term("Emulation")
                    }
                }
            };
        }

        private static Dictionary<string, List<Term>> BuildSpanish()
        {
            return new Dictionary<string, List<Term>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "genres",
                    new List<Term>
                    {
                        new Term("Accion", "Acción", "Action"),
                        new Term("Aventura", "Adventure"),
                        new Term("RPG", "Rol", "Role-playing"),
                        new Term("Estrategia", "Strategy"),
                        new Term("Simulacion", "Simulación", "Simulation"),
                        new Term("Deportes", "Sports"),
                        new Term("Carreras", "Racing"),
                        new Term("Lucha", "Fighting"),
                        new Term("Plataformas", "Platformer", "Platform"),
                        new Term("Puzzle"),
                        new Term("Disparos", "Shooter", "FPS"),
                        new Term("Terror", "Horror"),
                        new Term("Supervivencia", "Survival"),
                        new Term("Sigilo", "Stealth"),
                        new Term("Roguelike", "Rogue-like", "Roguelite"),
                        new Term("Mundo abierto", "Open world"),
                        new Term("Metroidvania"),
                        new Term("Novela visual", "Visual novel"),
                        new Term("Ritmo", "Rhythm")
                    }
                },
                {
                    "tags",
                    new List<Term>
                    {
                        new Term("Un jugador", "Single-player", "Single player", "Singleplayer"),
                        new Term("Multijugador", "Multiplayer", "Multi-player"),
                        new Term("Cooperativo", "Co-op", "Coop", "Cooperative"),
                        new Term("Cooperativo online", "Cooperativo en línea", "Cooperativo en linea", "Online co-op", "Online coop"),
                        new Term("Cooperativo local", "Local co-op", "Local coop"),
                        new Term("Competitivo", "Competitive"),
                        new Term("PvP"), new Term("PvE"),
                        new Term("Deduccion social", "Deducción social", "Social deduction"),
                        new Term("Exploracion", "Exploración", "Exploration"),
                        new Term("Construccion", "Construcción", "Building"),
                        new Term("Gestion", "Gestión", "Management"),
                        new Term("Crafteo", "Crafting"),
                        new Term("Historia profunda", "Deep story"),
                        new Term("Narrativo", "Narrative"),
                        new Term("Humor", "Comedy"),
                        new Term("Dificil", "Difícil", "Difficult"),
                        new Term("Casual"), new Term("Retro"), new Term("Anime"), new Term("Pixel art"),
                        new Term("Ciencia ficcion", "Ciencia ficción", "Science fiction", "Sci-fi"),
                        new Term("Fantasia", "Fantasía", "Fantasy"),
                        new Term("Cyberpunk"),
                        new Term("Postapocaliptico", "Postapocalíptico", "Post-apocalyptic"),
                        new Term("Sandbox"), new Term("Procedural")
                    }
                },
                {
                    "features",
                    new List<Term>
                    {
                        new Term("Un jugador", "Single-player", "Single player", "Singleplayer"),
                        new Term("Multijugador online", "Multijugador en línea", "Multijugador en linea", "Online multiplayer", "Online Multi-Player"),
                        new Term("Multijugador local", "Local multiplayer", "Local Multi-Player"),
                        new Term("Cooperativo online", "Cooperativo en línea", "Cooperativo en linea", "Online co-op", "Online Cooperative"),
                        new Term("Cooperativo local", "Local co-op", "Local Cooperative", "Coop local"),
                        new Term(
                            "Pantalla dividida",
                            "Split screen",
                            "Shared/Split Screen",
                            "Pantalla partida",
                            "Pantalla compartida",
                            "Pantalla partida/compartida",
                            "Pantalla Partida/Compartida",
                            "Coop. a pantalla (com)partida",
                            "Coop. A Pantalla (Com)Partida",
                            "Coop a pantalla compartida",
                            "Coop a pantalla partida"),
                        new Term("Soporte mando", "Controller support", "Full controller support", "Partial Controller Support", "Compatible con mandos"),
                        new Term("Logros", "Achievements", "Steam Achievements", "Logros de Steam", "Logros De", "Logros de"),
                        new Term("Guardado en la nube", "Cloud saves", "Steam Cloud", "Nube de Steam"),
                        new Term("Cromos de Steam", "Steam trading cards", "Trading Cards"),
                        new Term("Compatibilidad Steam Deck", "Steam Deck compatibility", "Steam Deck"),
                        new Term("Juego cruzado", "Cross-play", "Crossplay", "Cross-Platform Multiplayer"),
                        new Term("Editor de niveles", "Level editor"),
                        new Term("Modos PvP", "PvP", "PvP modes"),
                        new Term("Modos PvE", "PvE", "PvE modes"),
                        new Term("Compras integradas", "In-app purchases", "In-App Purchases", "IAP")
                    }
                },
                {
                    "categories",
                    new List<Term>
                    {
                        new Term("Favoritos", "Favorites"),
                        new Term("Pendientes", "Backlog"),
                        new Term("Completados", "Completed"),
                        new Term("Abandonados", "Abandoned"),
                        new Term("Para jugar en cooperativo", "Co-op games", "Coop games"),
                        new Term("Para jugar rapido", "Para jugar rápido", "Quick sessions"),
                        new Term("Para sesiones largas", "Long sessions"),
                        new Term("Relax", "Relaxing"),
                        new Term("Retos", "Challenges"),
                        new Term("Narrativos", "Narrative"),
                        new Term("Multijugador", "Multiplayer"),
                        new Term("Indie"),
                        new Term("Retro"),
                        new Term("Emulacion", "Emulación", "Emulation")
                    }
                }
            };
        }
    }
}
