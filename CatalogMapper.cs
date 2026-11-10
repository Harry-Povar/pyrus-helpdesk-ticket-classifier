using PyrusApiClient;


public class CatalogMapper
{
    private readonly List<CatalogItem> routingCatalog;
    private readonly List<CatalogItem> servicesCatalog;
    private readonly List<CatalogItem> typeCatalog;
    private readonly List<CatalogItem> priorityCatalog;


    public CatalogMapper(
        List<CatalogItem> routingCatalog,
        List<CatalogItem> servicesCatalog,
        List<CatalogItem> typeCatalog,
        List<CatalogItem> priorityCatalog)
    {
        this.routingCatalog = routingCatalog;
        this.servicesCatalog = servicesCatalog;
        this.typeCatalog = typeCatalog;
        this.priorityCatalog = priorityCatalog;
    }



    public long[] GetRoutingIds(List<string> routing)
    {
        return routingCatalog
            .Where(c => routing.Contains(c.Values[1]))
            .Select(c => c.Id)
            .ToArray();
    }



    public long[] GetServiceIds(string service)
    {
        return servicesCatalog
            .Where(c => c.Values[2] == service)
            .Select(c => c.Id)
            .ToArray();
    }



    public long[] GetTypeIds(string type)
    {
        return typeCatalog
            .Where(c => c.Values[0] == type)
            .Select(c => c.Id)
            .ToArray();
    }



    public long[] GetPriorityIds(string priority)
    {
        return priorityCatalog
            .Where(c => c.Values[0] == priority)
            .Select(c => c.Id)
            .ToArray();
    }
}