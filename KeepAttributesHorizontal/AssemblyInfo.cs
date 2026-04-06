using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ComVisible(false)]

[assembly: CommandClass(typeof(KeepAttributesHorizontal.MyCommands))]
[assembly: ExtensionApplication(typeof(KeepAttributesHorizontal.PluginExtension))]
