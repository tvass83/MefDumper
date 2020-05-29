using System.Collections.Generic;

namespace MefDumper.DataModel
{
    public class ReflectionComposablePart
    {
        public bool IsCreated;
        public string TypeName;
        public List<ImportDefinition> Imports = new List<ImportDefinition>();
        public List<ExportDefinition> Exports = new List<ExportDefinition>();
    }
}
