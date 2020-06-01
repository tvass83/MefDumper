using MefDumper.DataModel;
using System.Collections.Generic;

namespace MefDumper.Helpers
{
    public class ImportDefinitionComparer : IEqualityComparer<ImportDefinition>
    {
        public static ImportDefinitionComparer Instance = new ImportDefinitionComparer();

        public bool Equals(ImportDefinition x, ImportDefinition y)
        {
            return (x.ContractName == y.ContractName &&
                    x.RequiredTypeIdentity == y.RequiredTypeIdentity);
        }

        public int GetHashCode(ImportDefinition obj)
        {
            return obj.GetHashCode();
        }
    }
}
