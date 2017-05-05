using System.Linq;

namespace Visitor.Fody
{
    public partial class ModuleWeaver
    {

        public void CleanReferences()
        {
            var referenceToRemove = ModuleDefinition.AssemblyReferences.FirstOrDefault(x => x.Name == "Visitor");
            if (referenceToRemove == null)
            {
                LogInfo("\tNo reference to 'Visitor' found. References not modified.");
                return;
            }

            ModuleDefinition.AssemblyReferences.Remove(referenceToRemove);
            LogInfo("\tRemoving reference to 'Visitor'.");
        }
    }
}