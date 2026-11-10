using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pyrus.ApiClient.Requests;
using Pyrus.ApiClient.Requests.Builders;
using PyrusApiClient;
using System.Text.Json;
using System.Runtime.InteropServices;

class Program
{
    
    //Форма для операторов

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    const int STD_INPUT_HANDLE = -10;
    const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    static void DisableQuickEdit()
    {
        IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);

        if (GetConsoleMode(handle, out uint mode))
        {
            mode |= ENABLE_EXTENDED_FLAGS;
            mode &= ~ENABLE_QUICK_EDIT_MODE;

            SetConsoleMode(handle, mode);
        }
    }

    private static List<CatalogItem> routingCatalog = new();
    private static List<CatalogItem> servicesCatalog = new();
    private static List<CatalogItem> typeCatalog = new();
    private static List<CatalogItem> priorityCatalog = new();

    private static List<CatalogItem> clientCatalog = new();
    private static DateTime _lastAuth = DateTime.MinValue;

    private static async Task<bool> Authorize(PyrusClient pyrusClient, string? login, string? securityKey)
    {
        try
        {
            await pyrusClient.Auth(login, securityKey);
            _lastAuth = DateTime.UtcNow;
            Console.WriteLine($"{DateTime.Now}: Авторизация успешна");
            return true;
        }
        catch
        {
            Console.WriteLine($"{DateTime.Now}: Ошибка авторизации");
            return false;
        }
    }

    private static bool IsFieldEmpty(PyrusApiClient.Task task, int fieldId)
    {
        var field = task.Fields.FirstOrDefault(f => f.Id == fieldId);
        if (field == null) return true;
        var value = field.ToString();
        return string.IsNullOrWhiteSpace(value);
    }
    static async System.Threading.Tasks.Task Main()
    { 
        DisableQuickEdit();
        var configText = System.IO.File.ReadAllText("setting.json");
        using var config = JsonDocument.Parse(configText);
        var root = config.RootElement;
        //авторизация
        var login = root
            .GetProperty("Pyrus")
            .GetProperty("Login")
            .GetString();

        var securityKey = root
            .GetProperty("Pyrus")
            .GetProperty("SecurityKey")
            .GetString();
        var pyrusClient = new PyrusClient();
        var form = root
        .GetProperty("Forms")
        .GetProperty("Main");

        await Authorize(pyrusClient, login, securityKey);

        int CatalogRoutingId = root.GetProperty("Catalogs").GetProperty("Routing").GetInt32();
        int CatalogServiceId = root.GetProperty("Catalogs").GetProperty("Services").GetInt32();
        int CatalogTypeId = root.GetProperty("Catalogs").GetProperty("Type").GetInt32();
        int CatalogPriorityId = root.GetProperty("Catalogs").GetProperty("Priority").GetInt32();
        int CatalogClientsId = root.GetProperty("Catalogs").GetProperty("Clients").GetInt32();
        int BatchSize = root.GetProperty("Classifier").GetProperty("BatchSize").GetInt32();
        int _delay = root.GetProperty("Classifier").GetProperty("Delay").GetInt32();

        routingCatalog = (await pyrusClient.GetCatalog(CatalogRoutingId))?.Items ?? new List<CatalogItem>();
        servicesCatalog = (await pyrusClient.GetCatalog(CatalogServiceId))?.Items ?? new List<CatalogItem>();
        typeCatalog = (await pyrusClient.GetCatalog(CatalogTypeId))?.Items ?? new List<CatalogItem>();
        priorityCatalog = (await pyrusClient.GetCatalog(CatalogPriorityId))?.Items ?? new List<CatalogItem>();
        clientCatalog = (await pyrusClient.GetCatalog(CatalogClientsId))?.Items ?? new List<CatalogItem>();
        //Классификатор

        var modelConfig = root.GetProperty("Model");
        
        var onnxPath = modelConfig
            .GetProperty("OnnxPath")
            .GetString();

        var tokenizerPath = modelConfig
            .GetProperty("TokenizerPath")
            .GetString();

        var labelsPath = modelConfig
            .GetProperty("LabelsPath")
            .GetString();


        if (onnxPath == null || tokenizerPath == null || labelsPath == null)
        {
            Console.WriteLine("Ошибка: некорректный путь к модели в config.json");
            return;
        }

        var classifier = new TicketClassifier(
            onnxPath,
            tokenizerPath,
            labelsPath
        );

        var catalogMapper = new CatalogMapper(
            routingCatalog,
            servicesCatalog,
            typeCatalog,
            priorityCatalog
        );


        var clientFinder = new ClientFinder(
            clientCatalog
        );

        //Можно подумать, как сделать приостановку и запуск программы
        while (true)
        {
            bool flag_correct_proccessing = true;
            if (DateTime.UtcNow - _lastAuth >= TimeSpan.FromHours(23))
            {
                Console.WriteLine($"{DateTime.Now}: Выполняется переавторизация...");
                if (!await Authorize(pyrusClient, login, securityKey))
                {
                    await System.Threading.Tasks.Task.Delay(60000);
                    continue;
                }
            }
            try
            {
                await Work(pyrusClient, classifier, catalogMapper, clientFinder, BatchSize, form);
            }
            catch
            {
                flag_correct_proccessing = false;
                Console.WriteLine($"{DateTime.Now}: Проблемы с соединением!");
            }
            if (flag_correct_proccessing)
                await System.Threading.Tasks.Task.Delay(1000);
            else 
                await System.Threading.Tasks.Task.Delay(60000);
        }
    }

    private static async System.Threading.Tasks.Task Work(PyrusClient pyrusClient, TicketClassifier classifier, CatalogMapper catalogMapper, ClientFinder clientFinder, int batchSize, JsonElement form)
    {

        int FormId = form.GetProperty("Id").GetInt32();
        int RoutingFieldId = form.GetProperty("Routing").GetInt32();
        int ServicesFieldId = form.GetProperty("Services").GetInt32();
        int TypeFieldId = form.GetProperty("Type").GetInt32();
        int PriorityFieldId = form.GetProperty("Priority").GetInt32();
        int ClientFieldId = form.GetProperty("Client").GetInt32();
        int NameFieldId = form.GetProperty("Name").GetInt32();
        int MailFieldId = form.GetProperty("Mail").GetInt32();
        int SubjectFieldId = form.GetProperty("Subject").GetInt32();
        int DescriptionFieldId = form.GetProperty("Description").GetInt32();
        //Получаем реестр
        var registry = RequestBuilder.GetRegistry(FormId).OnSteps(1);
        var registryResult = await registry.Process(pyrusClient);
        if (registryResult.Tasks == null)
        {
            //Console.WriteLine($"{DateTime.Now}: Нет задач для обработки.");
            return;
        }
        var EmptyTasks = registryResult.Tasks 
        .Where(t =>
            IsFieldEmpty(t, RoutingFieldId) ||
            IsFieldEmpty(t, ServicesFieldId) ||
            IsFieldEmpty(t, TypeFieldId) ||
            IsFieldEmpty(t, PriorityFieldId))
        .ToList();

        if (EmptyTasks.Count == 0)
            return;
            

        var tickets = EmptyTasks
            .Select(t => string.Join(". ", new[]
            {
                t?.Fields?.FirstOrDefault(f => f.Id == SubjectFieldId)?.ToString() ?? String.Empty,
                t?.Fields?.FirstOrDefault(f => f.Id == DescriptionFieldId)?.ToString() ?? String.Empty
            }.Where(s => !string.IsNullOrWhiteSpace(s))))
            .ToList();

        var clientName = EmptyTasks
            .Select(t => t?.Fields?.FirstOrDefault(f => f.Id == NameFieldId)?.ToString() ?? String.Empty)
            .ToList();

        var clientMail = EmptyTasks
            .Select(t => t?.Fields?.FirstOrDefault(f => f.Id == MailFieldId)?.ToString() ?? String.Empty)
            .ToList();

        for (int start = 0; start < tickets.Count; start += batchSize)
        {
            var batch = tickets.Skip(start).Take(batchSize).ToList();
            var sw = Stopwatch.StartNew();
            List <ClassificationResult> results;
            try{
                results = classifier.PredictBatch(batch) ?? new List<ClassificationResult>();
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now}: Ошибка батча: {e.Message}");
                continue;
            }
            sw.Stop();
            Console.WriteLine($"{DateTime.Now}: Батч {start / batchSize + 1}: {batch.Count} заявок за {sw.Elapsed.TotalSeconds:F3} сек");

            for (int i = 0; i < results.Count; i++)
            {
                try {
                    var task = EmptyTasks[start + i];
                    var r = results[i];
                    
                    var routingIds = catalogMapper.GetRoutingIds(r.Routing);
                    var servicesIds = catalogMapper.GetServiceIds(r.Services);
                    var typeIds = catalogMapper.GetTypeIds(r.Type);
                    var priorityIds = catalogMapper.GetPriorityIds(r.Priority);
                    var clientId = clientFinder.Find(
                        clientName[start+i],
                        clientMail[start+i]
                    );
                    var mailValue = clientFinder.NormalizeMail(clientMail[start+i]);
                    var request = RequestBuilder.CommentFormTask(task.Id);

                    if (routingIds.Length > 0 && IsFieldEmpty(task, RoutingFieldId))
                        request.FieldUpdates.Add(FormFieldCatalog.Create<FormFieldCatalog>(RoutingFieldId)
                            .WithValues(routingIds));

                    if (servicesIds.Length > 0 && IsFieldEmpty(task, ServicesFieldId)) //Отравка результатов обработки по сервисам, приоритетам и типу задач
                        request.FieldUpdates.Add(FormFieldCatalog.Create<FormFieldCatalog>(ServicesFieldId)
                            .WithValues(servicesIds));

                    if (typeIds.Length > 0 && IsFieldEmpty(task, TypeFieldId))
                        request.FieldUpdates.Add(FormFieldCatalog.Create<FormFieldCatalog>(TypeFieldId)
                            .WithValues(typeIds));

                    if (priorityIds.Length > 0 && IsFieldEmpty(task, PriorityFieldId))
                        request.FieldUpdates.Add(FormFieldCatalog.Create<FormFieldCatalog>(PriorityFieldId)
                            .WithValues(priorityIds));

                    if (clientId.HasValue)
                        request.FieldUpdates.Add(FormFieldCatalog.Create<FormFieldCatalog>(ClientFieldId)
                            .WithValues(clientId.Value));
                    request.FieldUpdates.Add(
                        FormFieldText.Create<FormFieldText>(MailFieldId)
                            .WithValue(mailValue));
                    //request.Approve(); //На свой страх и риск
                    await request.Process(pyrusClient);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{DateTime.Now}: Ошибка в обработке задачи: {e.Message}");
                }
            }
        }
    }
}
