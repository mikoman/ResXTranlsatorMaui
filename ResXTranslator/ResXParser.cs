using System;
using System.Xml.Linq;

namespace ResXTranslator
{
	public class ResXParser 
	{
        public ResXParser()
        {

        }

        public Dictionary<string, string> ReadResXFile(string path)
        {
            var values = new Dictionary<string, string>();
            var doc = XDocument.Load(path);
            foreach (var dataElement in doc.Root.Elements("data"))
            {
                var nameAttribute = dataElement.Attribute("name");
                var valueElement = dataElement.Element("value");

                if (nameAttribute != null && valueElement != null)
                {
                    values[nameAttribute.Value] = valueElement.Value;
                }
            }
            return values;
        }

        public void WriteResXFile(string path, Dictionary<string, string> values)
        {
            var doc = new XDocument();
            var root = new XElement("root");
            doc.Add(root);

            foreach (var entry in values)
            {
                var dataElement = new XElement("data");
                dataElement.Add(new XAttribute("name", entry.Key));
                dataElement.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
                dataElement.Add(new XElement("value", entry.Value));
                root.Add(dataElement);
            }

            doc.Save(path);
        }
    }
}

