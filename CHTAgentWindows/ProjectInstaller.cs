using System.ComponentModel;
using System.Configuration.Install;

namespace CHTAgentWindows
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
        }
    }
}
