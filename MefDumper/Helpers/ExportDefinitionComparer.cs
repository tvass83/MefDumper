using MefDumper.DataModel;
using System.Collections.Generic;

namespace MefDumper.Helpers
{
    public class ExportDefinitionComparer : IEqualityComparer<ExportDefinition>
    {
        public static ExportDefinitionComparer Instance = new ExportDefinitionComparer();

        public bool Equals(ExportDefinition x, ExportDefinition y)
        {
            return (x.ContractName == y.ContractName &&
                    x.TypeIdentity == y.TypeIdentity);
        }

        public int GetHashCode(ExportDefinition obj)
        {
            return obj.GetHashCode();
        }
    }
}
