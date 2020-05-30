using MefDumper.DataModel;
using MefDumper.Helpers;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using WcfDumper.Helpers;

namespace MefDumper
{
    class Program
    {
        static void Main(string[] args)
        {
            var retCode = ArgParser.Parse(args, new string[] { }, new string[] { "-a", "-d" });

            ValidateArguments();

            if (retCode == ErrorCode.Success)
            {
                if (ArgParser.SwitchesWithValues.ContainsKey("-a"))
                {
                    var assemblies = ArgParser.SwitchesWithValues["-a"].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var asm in assemblies)
                    {
                        ValidateFile(asm);
                    }

                    var aggrCat = new AggregateCatalog(assemblies.Select(x => new AssemblyCatalog(x)));

                    var RESULT = new List<ReflectionComposablePart>();

                    using (var container = new CompositionContainer(aggrCat))
                    {
                        foreach (var part in container.Catalog.Parts)
                        {
                            var rfc = new ReflectionComposablePart();
                            rfc.TypeName = part.ToString();

                            foreach (var import in part.ImportDefinitions)
                            {
                                var impDef = new ImportDefinition();
                                impDef.ContractName = import.ContractName;

                                var s = import.ToString().Split(new string[] { "\n\t" }, StringSplitOptions.RemoveEmptyEntries);
                                impDef.RequiredTypeIdentity = s[1].Substring(s[1].IndexOf("\t") + 1);
                                rfc.Imports.Add(impDef);
                            }

                            foreach (var export in part.ExportDefinitions)
                            {
                                var expDef = new ExportDefinition();
                                expDef.ContractName = export.ContractName;
                                expDef.TypeIdentity = (string)export.Metadata[CONST_ExportTypeIdentity];
                                rfc.Exports.Add(expDef);
                            }

                            RESULT.Add(rfc);
                        }
                    }

                    DgmlHelper.CreateDgml($"d:\\temp\\dgml\\{Guid.NewGuid().ToString()}.dgml", RESULT);
                }
                else if (ArgParser.SwitchesWithValues.ContainsKey("-d"))
                {
                    string dumpFile = ArgParser.SwitchesWithValues["-d"];
                    ValidateFile(dumpFile);

                    var wrapper = ClrMdHelper.LoadDumpFile(dumpFile);
                    wrapper.TypesToDump.Add(TYPE_CompositionContainer);

                    wrapper.ClrHeapIsNotWalkableCallback = () =>
                    {
                        Console.WriteLine("Cannot walk the heap!");
                        //Console.WriteLine("PID: {0} - Cannot walk the heap!", pid);
                    };

                    wrapper.ClrObjectOfTypeFoundCallback = DumpTypes;

                    wrapper.Process();
                }
                else
                {
                    PrintSyntaxAndExit(retCode);
                }
            }
            else
            {

                PrintSyntaxAndExit(retCode);
            }
        }

        private static void ValidateArguments()
        {
            //TODO: validate various combinations
        }

        private static void ValidateFile(string path)
        {
            bool ret = File.Exists(path);

            if (!ret)
            {
                Console.WriteLine($"ERROR: The following file does not exist:");
                Console.WriteLine($"\t{path}");
                Environment.Exit(1);
            }
        }

        private static void PrintSyntaxAndExit(ErrorCode errorCode)
        {
            if (errorCode != ErrorCode.Success)
            {
                Console.WriteLine($"Syntax error: {errorCode}");
            }

            Console.WriteLine("Usage:");
            Console.WriteLine("MefDumper -d dumpfile");
            Console.WriteLine(" OR");
            Console.WriteLine("MefDumper -a assembly1[;assembly2;...;assemblyN]");

            Environment.Exit(1);
        }

        private static void DumpTypes(ClrHeap heap, ulong obj, string type)
        {
            Console.WriteLine($"Dumping CompositionContainer @{obj:X}");

            // Check if custom ExportProviders are present
            ulong providersFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_Providers);

            if (providersFieldValue != 0)
            {
                ulong providerArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, providersFieldValue, FIELD_List);
                List<ulong> providerObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(providerArrayObj), providerArrayObj);

                if (providerObjs.Count > 0)
                {
                    Console.WriteLine("WARNING: custom ExportProvider(s) were found:");
                }

                foreach (ulong provider in providerObjs)
                {
                    ClrType itemType = heap.GetObjectType(provider);
                    Console.WriteLine($"\t{itemType.Name}");
                }
            }

            var RESULT = new List<ReflectionComposablePart>();

            // Get ComposablePart[] from ComposablePartExportProvider
            List<ulong> composableParts = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, obj, HIERARCHY_CompositionContainer_To_ComposableParts, 0, TYPE_ComposablePart);

            foreach (var composablePart in composableParts)
            {
                string composablePartTypeName = heap.GetObjectType(composablePart).Name;

                if (composablePartTypeName == TYPE_ReflectionComposablePart)
                {
                    var rcpDef = ClrMdHelper.GetObjectAs<ulong>(heap, composablePart, FIELD_Definition);
                    var rcp = ProcessReflectionComposablePartDefinition(heap, rcpDef);
                    rcp.IsCreated = true;
                    RESULT.Add(rcp);
                }
                else if (composablePartTypeName == TYPE_SingleExportComposablePart)
                {
                    ulong export = ClrMdHelper.GetObjectAs<ulong>(heap, composablePart, FIELD_Export);
                    ulong exportedValue = ClrMdHelper.GetObjectAs<ulong>(heap, export, FIELD_ExportedValue);
                    string exportedValueTypeName = exportedValue != 0 ? heap.GetObjectType(exportedValue).Name : null;
                    bool isCreated = exportedValueTypeName != null && (exportedValueTypeName != typeof(object).FullName);

                    var exportDefinition = ClrMdHelper.GetObjectAs<ulong>(heap, export, FIELD_Definition);
                    string contract = ClrMdHelper.GetObjectAs<string>(heap, exportDefinition, FIELD_ContractName);
                    var metadata = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, exportDefinition, HIERARCHY_ExportDefinition_To_Metadata, 0, TYPE_KVP_String_Object);
                    string typeId = "";

                    foreach (var entry in metadata)
                    {
                        if (ClrMdHelper.GetStringContents(heap, entry.key) == CONST_ExportTypeIdentity)
                        {
                            typeId = ClrMdHelper.GetStringContents(heap, entry.value);
                            break;
                        }
                    }
                    
                    var rcp = new ReflectionComposablePart();
                    rcp.TypeName = isCreated ? exportedValueTypeName : typeId;                    
                    rcp.IsCreated = isCreated;
                    rcp.Exports.Add(new ExportDefinition() { ContractName = contract, TypeIdentity = typeId });

                    RESULT.Add(rcp);
                }
                else
                {
                    Console.WriteLine($"WARNING: Unsupported ComposablePart was found: {composablePartTypeName}");
                }
            }

            // Check if there exists a CatalogExportProvider
            ulong catalogExProvider = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_CatalogExportProvider);

            if (catalogExProvider != 0)
            {
                ulong catalogFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, catalogExProvider, FIELD_Catalog);
                HashSet<ulong> parts = new HashSet<ulong>();

                InvokeCatalogHandler(heap, catalogFieldValue, parts);                                

                foreach (var part in parts)
                {
                    var rcp = ProcessReflectionComposablePartDefinition(heap, part);

                    RESULT.Add(rcp);
                }

                Console.WriteLine($"{RESULT.Count} parts were found: ");
                foreach (var part in RESULT)
                {
                    Console.WriteLine($"\t{part.TypeName}");
                }

                Console.WriteLine();

                // Get activated auto-created parts
                var activePartObjs = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, catalogExProvider, HIERARCHY_CatalogExportProvider_To_Activated, 0, TYPE_ComposablePartDefinitionArray2);
                var activatedPartNames = new HashSet<string>();

                foreach (var activePart in activePartObjs)
                {
                    ulong partCreationInfo = ClrMdHelper.GetObjectAs<ulong>(heap, activePart.key, FIELD_CreationInfo);
                    string partType = InvokePartCreationInfoHandler(heap, partCreationInfo);
                    activatedPartNames.Add(partType);                    
                }

                foreach (var rcp in RESULT)
                {
                    if (activatedPartNames.Contains(rcp.TypeName))
                    {
                        rcp.IsCreated = true;
                    }
                }

                DgmlHelper.CreateDgml($"d:\\temp\\dgml\\{obj:X}.dgml", RESULT);
            }
        }

        private static ReflectionComposablePart ProcessReflectionComposablePartDefinition(ClrHeap heap, ulong part)
        {
            var rcp = new ReflectionComposablePart();

            ulong creationInfoObj = ClrMdHelper.GetLastObjectInHierarchy(heap, part, HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo, 0);
            ClrType creationInfoObjType = heap.GetObjectType(creationInfoObj);
            rcp.TypeName = creationInfoObjType.GetRuntimeType(creationInfoObj)?.Name ?? creationInfoObjType.Name;

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
                        Console.WriteLine($"WARNING: Special export was found with no type identity (contract: {contract})");
                        continue;
                    }

                    ClrType typeObjType = heap.GetObjectType(typeObj);
                    string typename = typeObjType.GetRuntimeType(typeObj)?.Name ?? typeObjType.Name;
                    rcp.Exports.Add(new ExportDefinition() { ContractName = contract, TypeIdentity = typename });
                }
            }

            return rcp;
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

            foreach (var partObj in partObjs)
            {
                parts.Add(partObj);
            }
        }

        private static void InvokeCatalogHandler(ClrHeap heap, ulong composablePartCatalog, HashSet<ulong> parts)
        {
            string catalogType = heap.GetObjectType(composablePartCatalog).Name;

            if (CatalogActionMappings.ContainsKey(catalogType))
            {
                CatalogActionMappings[catalogType](heap, composablePartCatalog, parts);
            }
            else
            {
                Console.WriteLine($"WARNING: unknown catalog type: '{catalogType}'");
            }
        }

        private static string ProcessAttributedPartCreationInfo(ClrHeap heap, ulong partCreationInfo)
        {
            ulong creationInfoObj = ClrMdHelper.GetObjectAs<ulong>(heap, partCreationInfo, FIELD_Type);
            ClrType creationInfoObjType = heap.GetObjectType(creationInfoObj);
            return creationInfoObjType.GetRuntimeType(creationInfoObj)?.Name ?? creationInfoObjType.Name;
        }
                
        private static string ProcessGenericSpecializationPartCreationInfo(ClrHeap heap, ulong partCreationInfo)
        {
            ulong creationInfoObj = ClrMdHelper.GetObjectAs<ulong>(heap, partCreationInfo, FIELD_OriginalPartCreationInfo);
            return InvokePartCreationInfoHandler(heap, creationInfoObj);
        }

        private static string InvokePartCreationInfoHandler(ClrHeap heap, ulong partCreationInfo)
        {
            string partCreationInfoType = heap.GetObjectType(partCreationInfo).Name;

            if (PartCreationInfoActionMappings.ContainsKey(partCreationInfoType))
            {
                return PartCreationInfoActionMappings[partCreationInfoType](heap, partCreationInfo);
            }
            else
            {
                Console.WriteLine($"WARNING: unknown PartCreationInfo type: '{partCreationInfoType}'");
                return null;
            }
        }

        private static string[] HIERARCHY_AggregateCatalog_To_ComposablePartCatalogs = new string[] { "_catalogs", "_catalogs", "_items" };
        private static string[] HIERARCHY_TypeCatalog_To_ComposablePartDefinitions = new string[] { "_parts", "_items" };
        private static string[] HIERARCHY_DirectoryCatalog_To_AssemblyCatalogs = new string[] { "_assemblyCatalogs", "entries" };

        private static string[] HIERARCHY_CatalogExportProvider_To_Activated = new string[] { "_activatedParts", "entries" };
        private static string[] HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo = new string[] { "_creationInfo", "_type" };
        private static string[] HIERARCHY_CompositionContainer_To_ComposableParts = new string[] { "_partExportProvider", "_parts", "_items" };
        private static string[] HIERARCHY_SingleExportComposablePart_To_ExportDefinition = new string[] { "_export", "_definition" };
        private static string[] HIERARCHY_ExportDefinition_To_Metadata = new string[] { "_metadata", "m_dictionary", "entries" };

        private static string FIELD_CatalogExportProvider = "_catalogExportProvider";
        private static string FIELD_Catalog = "_catalog";
        private static string FIELD_InnerCatalog = "_innerCatalog";
        private static string FIELD_Providers = "_providers";
        private static string FIELD_List = "list";
        private static string FIELD_Imports = "_imports";
        private static string FIELD_Export = "_export";
        private static string FIELD_Exports = "_exports";
        private static string FIELD_ContractName = "_contractName";
        private static string FIELD_RequiredTypeIdentity = "_requiredTypeIdentity";
        private static string FIELD_TypeIdentityType = "_typeIdentityType";
        private static string FIELD_ExportDefinition = "_exportDefinition";
        private static string FIELD_Definition = "_definition";
        private static string FIELD_ExportedValue = "_exportedValue";
        private static string FIELD_CreationInfo = "_creationInfo";
        private static string FIELD_Type = "_type";
        private static string FIELD_OriginalPartCreationInfo = "_originalPartCreationInfo";

        private const string CONST_ExportTypeIdentity = "ExportTypeIdentity";

        private static string TYPE_CompositionContainer = "System.ComponentModel.Composition.Hosting.CompositionContainer";
        private static string TYPE_ComposablePartDefinitionArray = "System.ComponentModel.Composition.Primitives.ComposablePartDefinition[]";
        private static string TYPE_ComposablePartDefinitionArray2 = "System.Collections.Generic.Dictionary+Entry<System.ComponentModel.Composition.Primitives.ComposablePartDefinition,System.ComponentModel.Composition.Hosting.CatalogExportProvider+CatalogPart>[]";
        private static string TYPE_ExportProviderArray = "System.ComponentModel.Composition.Hosting.ExportProvider[]";
        private static string TYPE_ComposablePartCatalogArray = "System.ComponentModel.Composition.Primitives.ComposablePartCatalog[]";
        private static string TYPE_KVP_String_AssemblyCatalog = "System.Collections.Generic.Dictionary+Entry<System.String,System.ComponentModel.Composition.Hosting.AssemblyCatalog>[]";
        private static string TYPE_ImportDefinitionArray = "System.ComponentModel.Composition.Primitives.ImportDefinition[]";
        private static string TYPE_ExportDefinitionArray = "System.ComponentModel.Composition.Primitives.ExportDefinition[]";
        private static string TYPE_ComposablePart = "System.ComponentModel.Composition.Primitives.ComposablePart[]";
        private static string TYPE_ReflectionComposablePartDefinition = "System.ComponentModel.Composition.ReflectionModel.ReflectionComposablePartDefinition";
        private static string TYPE_ReflectionComposablePart = "System.ComponentModel.Composition.ReflectionModel.ReflectionComposablePart";
        private static string TYPE_SingleExportComposablePart = "System.ComponentModel.Composition.Hosting.CompositionBatch+SingleExportComposablePart";
        private static string TYPE_KVP_String_Object = "System.Collections.Generic.Dictionary+Entry<System.String,System.Object>[]";
        private static string TYPE_GenericSpecializationPartCreationInfo = "System.ComponentModel.Composition.ReflectionModel.GenericSpecializationPartCreationInfo";
        private static string TYPE_AttributedPartCreationInfo = "System.ComponentModel.Composition.AttributedModel.AttributedPartCreationInfo";

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

        private static Dictionary<string, Func<ClrHeap, ulong, string>> PartCreationInfoActionMappings = new Dictionary<string, Func<ClrHeap, ulong, string>>
        {
            { TYPE_AttributedPartCreationInfo, ProcessAttributedPartCreationInfo },
            { TYPE_GenericSpecializationPartCreationInfo, ProcessGenericSpecializationPartCreationInfo }
        };
    }
}
