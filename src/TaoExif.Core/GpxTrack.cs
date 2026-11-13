using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TaoExif.Core;

/// <summary>A track point from a GPX log.</summary>
public readonly record struct TrackPoint(double Latitude, double Longitude, DateTimeOffset? Time);

/// <summary>Minimal GPX reader, only used to draw a track log on the map (exiftool does the geotagging).</summary>
public static class GpxTrack
{
    public static List<TrackPoint> Load(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        var doc = XDocument.Load(reader);
        var points = new List<TrackPoint>();
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "trkpt" or "rtept"))
        {
            if (!double.TryParse((string?)el.Attribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                || !double.TryParse((string?)el.Attribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                continue;
            DateTimeOffset? time = null;
            var t = el.Elements().FirstOrDefault(e => e.Name.LocalName == "time");
            if (t != null && DateTimeOffset.TryParse(t.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                time = parsed;
            points.Add(new TrackPoint(lat, lon, time));
        }
        return points;
    }
}
