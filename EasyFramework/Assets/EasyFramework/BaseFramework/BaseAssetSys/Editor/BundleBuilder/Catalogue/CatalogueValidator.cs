using System.Collections.Generic;

public static class CatalogueValidator
{
    public class LoadPathDuplicate
    {
        public string loadPath;
        public string firstAssetPath;
        public string secondAssetPath;
    }

    public class ValidationResult
    {
        public List<LoadPathDuplicate> loadPathDuplicates = new List<LoadPathDuplicate>();
        public bool hasErrors;
        public bool hasWarnings;
    }

    public static ValidationResult ValidateEntries(IEnumerable<AssetCatalogEntry> entries, string resourceRoot, bool duplicateAsError)
    {
        var result = new ValidationResult();
        if (entries == null)
            return result;

        var loadPathMap = new Dictionary<string, string>();

        foreach (AssetCatalogEntry entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                continue;

            string loadPath = CatalogueReader.ToLoadPath(entry.assetPath, resourceRoot);
            if (string.IsNullOrEmpty(loadPath))
                continue;

            if (loadPathMap.TryGetValue(loadPath, out string firstAssetPath))
            {
                result.loadPathDuplicates.Add(new LoadPathDuplicate
                {
                    loadPath = loadPath,
                    firstAssetPath = firstAssetPath,
                    secondAssetPath = entry.assetPath
                });

                if (duplicateAsError)
                    result.hasErrors = true;
                else
                    result.hasWarnings = true;
            }
            else
            {
                loadPathMap[loadPath] = entry.assetPath;
            }
        }

        return result;
    }
}
