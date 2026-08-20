using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Xml.Linq;

namespace MetaDataIAPlugin
{
    internal static class SvgIconGeometryLoader
    {
        private static readonly Dictionary<string, SvgIconGeometry> Cache =
            new Dictionary<string, SvgIconGeometry>(StringComparer.OrdinalIgnoreCase);

        public static SvgIconGeometry GetGeometry(string fileName)
        {
            var safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return SvgIconGeometry.Empty;
            }

            SvgIconGeometry cached;
            if (Cache.TryGetValue(safeName, out cached))
            {
                return cached;
            }

            try
            {
                var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var path = ResolveSvgPath(directory, safeName);
                if (string.IsNullOrEmpty(path))
                {
                    Cache[safeName] = SvgIconGeometry.Empty;
                    return SvgIconGeometry.Empty;
                }

                var document = XDocument.Load(path);
                var pathElements = document.Descendants()
                    .Where(a => string.Equals(a.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
                    .Where(a => !string.Equals((string)a.Attribute("stroke"), "none", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var data = string.Join(" ", pathElements
                    .Select(a => (string)a.Attribute("d"))
                    .Where(a => !string.IsNullOrWhiteSpace(a)));

                var evenOdd = pathElements.Any(a =>
                    string.Equals((string)a.Attribute("fill-rule"), "evenodd", StringComparison.OrdinalIgnoreCase));

                double canvasWidth = 24;
                double canvasHeight = 24;
                var svg = document.Root;
                if (svg != null)
                {
                    var viewBox = (string)svg.Attribute("viewBox");
                    if (!string.IsNullOrWhiteSpace(viewBox))
                    {
                        var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 4)
                        {
                            double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out canvasWidth);
                            double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out canvasHeight);
                        }
                    }
                }

                cached = new SvgIconGeometry(data, evenOdd, canvasWidth, canvasHeight);
            }
            catch
            {
                cached = SvgIconGeometry.Empty;
            }

            Cache[safeName] = cached;
            return cached;
        }

        public static string GetPathData(string fileName)
        {
            return GetGeometry(fileName).PathData;
        }

        private static string ResolveSvgPath(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var candidate = Path.Combine(directory, "Icons", fileName);
            return File.Exists(candidate) ? candidate : null;
        }

        internal sealed class SvgIconGeometry
        {
            public static readonly SvgIconGeometry Empty = new SvgIconGeometry(string.Empty, false, 24, 24);

            public SvgIconGeometry(string pathData, bool evenOdd, double canvasWidth, double canvasHeight)
            {
                PathData = pathData ?? string.Empty;
                EvenOdd = evenOdd;
                CanvasWidth = canvasWidth > 0 ? canvasWidth : 24;
                CanvasHeight = canvasHeight > 0 ? canvasHeight : 24;
            }

            public string PathData { get; private set; }
            public bool EvenOdd { get; private set; }
            public double CanvasWidth { get; private set; }
            public double CanvasHeight { get; private set; }

            public Geometry CreateGeometry()
            {
                if (string.IsNullOrWhiteSpace(PathData))
                {
                    return Geometry.Empty;
                }

                var pathGeometry = new PathGeometry
                {
                    FillRule = EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero,
                    Figures = PathFigureCollection.Parse(PathData)
                };
                return pathGeometry;
            }
        }
    }
}
