using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace LihuatLib.Core
{
    internal static class ConsumerModDeclaration
    {
        internal static bool UsesLibrary(ModContentPack mod)
        {
            string path = Path.Combine(mod.RootDir, "About", "About.xml");
            if (!File.Exists(path)) return false;
            try
            {
                // RimWorld removes dependencies without download URLs from ModMetaData.
                using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    XElement root = XDocument.Load(reader).Root;
                    XElement versioned = root?.Element("modDependenciesByVersion")?.Elements().FirstOrDefault(element =>
                        string.Equals(element.Name.LocalName, "v" + VersionControl.CurrentVersionStringWithoutBuild, StringComparison.OrdinalIgnoreCase));
                    XElement dependencies = versioned ?? root?.Element("modDependencies");
                    return dependencies?.Elements("li").Any(dependency => string.Equals(
                        dependency.Element("packageId")?.Value.Trim(), "lihuat.lib", StringComparison.OrdinalIgnoreCase)) == true;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is XmlException)
            {
                Log.Warning("[LihuatLib] Cannot read consumer declaration for " + mod.PackageId + ": " + error.Message);
                return false;
            }
        }
    }
}
