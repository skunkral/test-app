using Viana.Core.Interfaces;
using Viana.Core.Models;

namespace Viana.Infrastructure.Services
{
    public class ManifestManager : IUpdateManager
    {
        public async Task CheckForUpdatesAsync()
        {
            // TODO: Implement logic to fetch manifest.json and compare hashes
        }
    }
}