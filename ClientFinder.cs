using PyrusApiClient;
using Pyrus.ApiClient.Requests.Builders;
public class ClientFinder
{

    private readonly List<CatalogItem> clientCatalog;


    public ClientFinder(List<CatalogItem> clientCatalog)
    {
        this.clientCatalog = clientCatalog;
    }



    public long? Find(
        string clientName,
        string clientMail)
    {

        var nameTokens = clientName
            .Trim()
            .ToLowerInvariant()
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            )
            .ToList();



        var mailTokens = clientMail
            .Split(
                new[]{" ", ","},
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(
                m => m.Trim().ToLowerInvariant()
            )
            .Select(m => m.Split('@')[0])
            .Where(
                m =>
                m != "help@academcity.online" &&
                m != "helpdesk@academcity.online"
            )
            .ToList();


        var mailValue = string.Join(
            ", ",
            mailTokens
        );
        var candidates = clientCatalog
            .Where(c =>
            {
                if (
                    c.Values.Count == 0 ||
                    string.IsNullOrWhiteSpace(c.Values[0])
                )
                    return false;



                var catalogTokens =
                    c.Values[0]
                    .Trim()
                    .ToLowerInvariant()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                int matches = nameTokens.Count(nt =>
                    catalogTokens.Any(ct => nt == ct)// && nt != "Приемная" && nt != "комиссия" && nt!= "АНО")
                );
                //if (matches>=1)
                //    Console.WriteLine($"Совпадений: {matches}, {c.Values[0]}");
                return matches >= 2;
            })
            .ToList();



        if (candidates.Count == 1)
        {
            return candidates[0].Id;
        }


        var byMail = clientCatalog
            .Where(c =>
                c.Values.Count > 1 &&
                !string.IsNullOrWhiteSpace(c.Values[1]) &&
                string.Equals(
                    c.Values[1].Trim().Split('@')[0],
                    mailValue,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();



        if(byMail.Count == 1)
        {
            return byMail[0].Id;
        }



        return null;
    }
    public string NormalizeMail(string clientMail)
    {
        var mailTokens = clientMail
            .Split(
                new[]{" ", ","},
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(
                m => m.Trim().ToLowerInvariant()
            )
            .Where(
                m =>
                m != "help@academcity.online" &&
                m != "helpdesk@academcity.online"
            )
            .ToList();


        return string.Join(", ", mailTokens);
    }
}



