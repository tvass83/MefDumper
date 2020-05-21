using MefDumper.DataModel;
using MefDumper.Helpers;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using WcfDumper.Helpers;

namespace MefDumper
{
    class Program
    {
        static void Main(string[] args)
        {
            //var wrapper = ClrMdHelper.LoadDumpFile(@"d:\MefDumper.DMP");
            //var wrapper = ClrMdHelper.LoadDumpFile(@"d:\mef_devenv.dmp");
            var wrapper = ClrMdHelper.LoadDumpFile(@"d:\common3.DMP");

            wrapper.TypesToDump.Add(TYPE_CompositionContainer);

            wrapper.ClrHeapIsNotWalkableCallback = () =>
            {
                Console.WriteLine("Cannot walk the heap!");
                //Console.WriteLine("PID: {0} - Cannot walk the heap!", pid);
            };

            wrapper.ClrObjectOfTypeFoundCallback = DumpTypes;

            wrapper.Process();
        }

        private static void DumpTypes(ClrHeap heap, ulong obj, string type)
        {
            // Check if custom ExportProviders are present
            ulong providersFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_Providers);

            if (providersFieldValue != 0)
            {
                ulong providerArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, providersFieldValue, FIELD_List);
                List<ulong> providerObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(providerArrayObj), providerArrayObj);

                if (providerObjs.Count > 0)
                {
                    Console.WriteLine("Custom ExportProvider(s):");
                }

                foreach (ulong provider in providerObjs)
                {
                    ClrType itemType = heap.GetObjectType(provider);
                    Console.WriteLine($"\t{itemType.Name}");
                }
            }

            // Check if there exists a CatalogExportProvider
            ulong catalogExProvider = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_CatalogExportProvider);

            if (catalogExProvider != 0)
            {
                ulong catalogFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, catalogExProvider, FIELD_Catalog);
                HashSet<ulong> parts = new HashSet<ulong>();

                try
                {
                    InvokeCatalogHandler(heap, catalogFieldValue, parts);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    return;
                }

                var RESULT = new List<ReflectionComposablePart>();

                foreach (var part in parts)
                {
                    var rcp = new ReflectionComposablePart();

                    ulong creationInfoObj = ClrMdHelper.GetLastObjectInHierarchy(heap, part, HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo, 0);
                    rcp.TypeName = heap.GetObjectType(creationInfoObj).GetRuntimeType(creationInfoObj).Name;

                    // Get ImportDefinition[]
                    ulong importArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, part, FIELD_Imports);

                    if (importArrayObj != 0)
                    {
                        List<ulong> importObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(importArrayObj), importArrayObj);

                        foreach (var importDef in importObjs)
                        {
                            string contract = ClrMdHelper.GetObjectAs<string>(heap, importDef, FIELD_ContractName);
                            string typeId = ClrMdHelper.GetObjectAs<string>(heap, importDef, FIELD_RequiredTypeIdentity);
                            rcp.Imports.Add(new ImportDefinition() { ContractName = contract, RequiredTypeIdentity = typeId });
                        }
                    }

                    // Get ExportDefinition[]
                    ulong exportArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, part, FIELD_Exports);

                    if (exportArrayObj != 0)
                    {
                        List<ulong> exportObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(exportArrayObj), exportArrayObj);

                        foreach (var exportDef in exportObjs)
                        {
                            ulong attrExpDef = ClrMdHelper.GetObjectAs<ulong>(heap, exportDef, FIELD_ExportDefinition);
                            string contract = ClrMdHelper.GetObjectAs<string>(heap, attrExpDef, FIELD_ContractName);
                            ulong typeObj = ClrMdHelper.GetObjectAs<ulong>(heap, attrExpDef, FIELD_TypeIdentityType);

                            if (typeObj == 0)
                            {
                                Console.WriteLine($"Special export was found with no type identity");
                                continue;
                            }

                            string typename = heap.GetObjectType(typeObj).GetRuntimeType(typeObj).Name;

                            rcp.Exports.Add(new ExportDefinition() { ContractName = contract, TypeIdentity = typename });
                        }
                    }

                    RESULT.Add(rcp);
                }

                DgmlHelper.CreateDgml($"d:\\temp\\dgml\\{Guid.NewGuid().ToString()}.dgml", RESULT);
            }

            // Get activated auto-created parts
            //var activePartObjs = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, obj, HIERARCHY_CompositionContainer_To_Activated, 0, TYPE_ComposablePartDefinitionArray2);
        }

        private static void ProcessAggregateCatalog(ClrHeap heap, ulong aggrCat, HashSet<ulong> parts)
        {
            List<ulong> catalogs = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, aggrCat, HIERARCHY_AggregateCatalog_To_ComposablePartCatalogs, 0, TYPE_ComposablePartCatalogArray);

            foreach (var catalog in catalogs)
            {
                // It can be any derivation of ComposablePartCatalog
                InvokeCatalogHandler(heap, catalog, parts);
            }
        }

        private static void ProcessDirectoryCatalog(ClrHeap heap, ulong dirCat, HashSet<ulong> parts)
        {
            var asmCatalogs = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, dirCat, HIERARCHY_DirectoryCatalog_To_AssemblyCatalogs, 0, TYPE_KVP_String_AssemblyCatalog);

            foreach (var catalog in asmCatalogs)
            {
                Console.WriteLine(catalog.key);
                // It's always an AssemblyCatalog
                InvokeCatalogHandler(heap, catalog.value, parts);
            }
        }

        private static void ProcessAssemblyCatalog(ClrHeap heap, ulong asmCat, HashSet<ulong> parts)
        {
            // It's always a TypeCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, asmCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessApplicationCatalog(ClrHeap heap, ulong appCat, HashSet<ulong> parts)
        {
            // It's always an AggregateCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, appCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessFilteredCatalog(ClrHeap heap, ulong filteredCat, HashSet<ulong> parts)
        {
            // It can be any derivation of ComposablePartCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, filteredCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessTypeCatalog(ClrHeap heap, ulong typeCat, HashSet<ulong> parts)
        {
            // Get all auto-created partdescriptions (ReflectionComposablePartDefinition[])
            List<ulong> partObjs = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, typeCat, HIERARCHY_TypeCatalog_To_ComposablePartDefinitions, 0, TYPE_ComposablePartDefinitionArray);

            if (partObjs.Count > 0)
            {
                Console.WriteLine("Parts:");
            }

            foreach (var partObj in partObjs)
            {
                parts.Add(partObj);                
            }
        }

        private static void InvokeCatalogHandler(ClrHeap heap, ulong composablePartCatalog, HashSet<ulong> parts)
        {
            string catalogType = heap.GetObjectType(composablePartCatalog).Name;
            CatalogActionMappings[catalogType](heap, composablePartCatalog, parts);
        }

        private static string[] HIERARCHY_AggregateCatalog_To_ComposablePartCatalogs = new string[] { "_catalogs", "_catalogs", "_items" };
        private static string[] HIERARCHY_TypeCatalog_To_ComposablePartDefinitions = new string[] { "_parts", "_items" };
        private static string[] HIERARCHY_DirectoryCatalog_To_AssemblyCatalogs = new string[] { "_assemblyCatalogs", "entries" };

        private static string[] HIERARCHY_CatalogExportProvider_To_Activated = new string[] { "_activatedParts", "entries" };
        private static string[] HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo = new string[] { "_creationInfo", "_type" };
        private static string[] HIERARCHY_CompositionContainer_To_ExportProviders = new string[] { "" };

        private static string FIELD_CatalogExportProvider = "_catalogExportProvider";
        private static string FIELD_Catalog = "_catalog";
        private static string FIELD_InnerCatalog = "_innerCatalog";
        private static string FIELD_Providers = "_providers";
        private static string FIELD_List = "list";
        private static string FIELD_Imports = "_imports";
        private static string FIELD_Exports = "_exports";
        private static string FIELD_ContractName = "_contractName";
        private static string FIELD_RequiredTypeIdentity = "_requiredTypeIdentity";
        private static string FIELD_TypeIdentityType = "_typeIdentityType";
        private static string FIELD_ExportDefinition = "_exportDefinition";

        private static string TYPE_CompositionContainer = "System.ComponentModel.Composition.Hosting.CompositionContainer";
        private static string TYPE_ComposablePartDefinitionArray = "System.ComponentModel.Composition.Primitives.ComposablePartDefinition[]";
        private static string TYPE_ComposablePartDefinitionArray2 = "System.Collections.Generic.Dictionary+Entry<System.ComponentModel.Composition.Primitives.ComposablePartDefinition,System.ComponentModel.Composition.Hosting.CatalogExportProvider+CatalogPart>[]";
        private static string TYPE_ExportProviderArray = "System.ComponentModel.Composition.Hosting.ExportProvider[]";
        private static string TYPE_ComposablePartCatalogArray = "System.ComponentModel.Composition.Primitives.ComposablePartCatalog[]";
        private static string TYPE_KVP_String_AssemblyCatalog = "System.Collections.Generic.Dictionary+Entry<System.String,System.ComponentModel.Composition.Hosting.AssemblyCatalog>[]";
        private static string TYPE_ImportDefinitionArray = "System.ComponentModel.Composition.Primitives.ImportDefinition[]";
        private static string TYPE_ExportDefinitionArray = "System.ComponentModel.Composition.Primitives.ExportDefinition[]";

        private static string TYPE_ReflectionComposablePartDefinition = "System.ComponentModel.Composition.ReflectionModel.ReflectionComposablePartDefinition";

        private static string TYPE_ApplicationCatalog = "System.ComponentModel.Composition.Hosting.ApplicationCatalog";
        private static string TYPE_AggregateCatalog = "System.ComponentModel.Composition.Hosting.AggregateCatalog";
        private static string TYPE_DirectoryCatalog = "System.ComponentModel.Composition.Hosting.DirectoryCatalog";
        private static string TYPE_AssemblyCatalog = "System.ComponentModel.Composition.Hosting.AssemblyCatalog";
        private static string TYPE_TypeCatalog = "System.ComponentModel.Composition.Hosting.TypeCatalog";
        private static string TYPE_FilteredCatalog = "System.ComponentModel.Composition.Hosting.FilteredCatalog";


        //System.ComponentModel.Composition.Hosting.ApplicationCatalog => "_innerCatalog" => link to AggregateCatalog
        //System.ComponentModel.Composition.Hosting.AggregateCatalog => "_catalogs", "_catalogs", "_items" => this includes 'n' other catalogs 
        //System.ComponentModel.Composition.Hosting.DirectoryCatalog => "_assemblyCatalogs", "entries" => kvp(filepath, AssemblyCatalog)
        //System.ComponentModel.Composition.Hosting.AssemblyCatalog => "_innerCatalog" => link to TypeCatalogs 
        //System.ComponentModel.Composition.Hosting.TypeCatalog => "_parts", "_items"
        //System.ComponentModel.Composition.Hosting.FilteredCatalog => "_innerCatalog" => link to any ComposablePartCatalog

        private static Dictionary<string, Action<ClrHeap, ulong, HashSet<ulong>>> CatalogActionMappings = new Dictionary<string, Action<ClrHeap, ulong, HashSet<ulong>>>
        {
            { TYPE_FilteredCatalog, ProcessFilteredCatalog },
            { TYPE_ApplicationCatalog, ProcessApplicationCatalog },
            { TYPE_AggregateCatalog, ProcessAggregateCatalog },
            { TYPE_DirectoryCatalog, ProcessDirectoryCatalog },
            { TYPE_AssemblyCatalog, ProcessAssemblyCatalog },
            { TYPE_TypeCatalog, ProcessTypeCatalog }
        };
    }
}
