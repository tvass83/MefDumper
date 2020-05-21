using MefDumper.DataModel;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace MefDumper.Helpers
{
    public static class DgmlHelper
    {
        public static void CreateDgml(string path, List<ReflectionComposablePart> parts)
        {
            var settings = new XmlWriterSettings();
            settings.ConformanceLevel = ConformanceLevel.Document;
            settings.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartElement("DirectedGraph", "http://schemas.microsoft.com/vs/2009/dgml");
                writer.WriteAttributeString("GraphDirection", "TopToBottom");
                writer.WriteAttributeString("Layout", "Sugiyama");
                writer.WriteAttributeString("Title", "Test");
                writer.WriteAttributeString("ZoomLevel", "-1");

                writer.WriteStartElement("Nodes");

                foreach (var part in parts)
                {
                    writer.WriteStartElement("Node");
                    writer.WriteAttributeString("Id", part.TypeName);
                    writer.WriteAttributeString("Label", part.TypeName);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();

                writer.WriteStartElement("Links");

                foreach (var part in parts)
                {
                    foreach (var import in part.Imports)
                    {
                        foreach (var innerPart in parts)
                        {
                            foreach (var export in innerPart.Exports)
                            {
                                if (export.ContractName == import.ContractName &&
                                    export.TypeIdentity == import.RequiredTypeIdentity)
                                {
                                    writer.WriteStartElement("Link");
                                    writer.WriteAttributeString("Source", part.TypeName);
                                    writer.WriteAttributeString("Target", innerPart.TypeName);
                                    writer.WriteEndElement();
                                }
                            }
                        }
                     }
                }

                writer.WriteEndElement();

                writer.WriteEndElement();

                writer.Flush();
            }
        }
    }
}
