**MefDumper** is a command-line tool (based on "[CLR MD](https://github.com/microsoft/clrmd)") that aims to dump MEF [CompositionContainer](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.compositioncontainer?view=netframework-4.8) objects in order to create directed graphs that represent the dependencies among the various parts within the container.
The tool supports the following scenarios.

#### Dumping managed assemblies

In this scenario, an [AssemblyCatalog](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.assemblycatalog?view=netframework-4.8) is created for all the provided assemblies who in turn are passed to an [AggregateCatalog](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.aggregatecatalog?view=netframework-4.8). It's important to provide all the related assemblies that take part in the composition else there will be many orphan parts in the resulting digraph. Exports and imports are identified by their corresponsing attributes, [ExportAttribute](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.exportattribute?view=netframework-4.8) and [ImportAttribute](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.importattribute?view=netframework-4.8).

Usage:
```
MefDumper -a test.dll[;test2.dll;...;testN.dll]
```

#### Dumping full memory dumps

In this scenario, MefDumper extracts additional information compared to the previous scenario. These are:

* Parts that have been instantiated (green boxes)
* Objects that were put into the container directly, via APIs like ComposeExportedValue (see Limitations)
* The type names of custom ExportProviders

Usage:
```
MefDumper -d test.dmp
```

#### Dumping a live process

In this scenario, the same pieces of information are extracted as in the previous scenario.

Usage:
```
MefDumper -pid <processId>
```

#### Limitations
* For extensibility reasons, CompositionContainer [supports](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.compositioncontainer.-ctor?view=netframework-4.8) the concept of custom [ExportProviders](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.exportprovider?view=netframework-4.8) whose structure unfortunately can't be predicted. This approach is typically used in advanced scenarios, like when Visual Studio uses [its own](https://github.com/microsoft/vs-mef) MEF implementation.

* There are some APis like [ComposeExportedValue](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.attributedmodelservices.composeexportedvalue?view=netframework-4.8) that allow you to put object instances into containers directly. When dumping managed assemblies such (hidden) exports can't be recognized. In the other scenarios however, you'll get full support for these objects.

* There are some APis like [GetExportedValue](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.composition.hosting.exportprovider.getexportedvalue?view=netframework-4.8) that allow you to retrieve objects from containers directly. Such (hidden) dependencies can't be recognized, unless you explicitly tell MEF about them. Given the following "hidden" dependency:
	```
	ComponentA:
	var instanceOfB = CompositionContainer.GetExportedValue<IComponentB>("ContractOfComponentB");
	```
	One can add the following field to ComponentA to let MEF know about the dependency:
	```
	[Import("ContractOfComponentB", AllowDefault = true)]
	private Lazy<IComponentB> _componentB;
	```
	
#### Displaying directed graphs
Graph drawing and visualization is an extremely complex topic. (See this [handbook](http://cs.brown.edu/people/rtamassi/gdhandbook/) for a glimpse). That's why MefDumper creates [Directed Graph Markup Language (DGML)](https://docs.microsoft.com/en-us/visualstudio/modeling/directed-graph-markup-language-dgml-reference?view=vs-2019) files so that it can utilize the existing tooling out there. 

I'd recommend using the DGML editor that [ships with Visual Studio 2017+](https://docs.microsoft.com/en-us/visualstudio/modeling/what-s-new-for-design-in-visual-studio?view=vs-2017#edition-support-for-architecture-and-modeling-tools). (You might have to install it from the Visual Studio installer "Individual components" tab.)
