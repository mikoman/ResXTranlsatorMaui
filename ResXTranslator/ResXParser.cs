using System.Xml.Linq;

namespace ResXTranslator;

public class ResXParser
{
    const string ResXReader = "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
    const string ResXWriter = "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    public Dictionary<string, string> ReadResXFile(string path)
    {
        var values = new Dictionary<string, string>();
        var doc = XDocument.Load(path);
        var root = doc.Root;

        if (root is null)
        {
            return values;
        }

        foreach (var dataElement in root.Elements("data"))
        {
            var nameAttribute = dataElement.Attribute("name");
            var valueElement = dataElement.Element("value");

            // Skip binary/typed resources: only plain strings can be translated.
            if (nameAttribute is null || valueElement is null || dataElement.Attribute("type") is not null)
            {
                continue;
            }

            values[nameAttribute.Value] = valueElement.Value;
        }

        return values;
    }

    public void WriteResXFile(string path, Dictionary<string, string> values)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var root = new XElement("root",
            ResHeader("resmimetype", "text/microsoft-resx"),
            ResHeader("version", "2.0"),
            ResHeader("reader", ResXReader),
            ResHeader("writer", ResXWriter));

        foreach (var entry in values)
        {
            root.Add(new XElement("data",
                new XAttribute("name", entry.Key),
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", entry.Value)));
        }

        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(path);
    }

    static XElement ResHeader(string name, string value) =>
        new("resheader", new XAttribute("name", name), new XElement("value", value));
}
