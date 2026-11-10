using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

public class TicketClassifier
{
    private readonly InferenceSession _session;
    private readonly XLMRobertaTokenizer _tokenizer;
    private readonly LabelsRoot _labels;
    private readonly string _routingTask;

    private readonly List<string> _singleTasks;
    private const int MaxLen = 256;
    private const float THRESHOLD = 0.5f;
    
    public TicketClassifier(string onnxPath, string vocabPath, string labelsPath)
    {
         // Настройки выполнения ONNX-модели
        var sessionOptions = new SessionOptions
        {
            // Число потоков для выполнения операций внутри модели
            IntraOpNumThreads = Environment.ProcessorCount,  
             // Число потоков для координации независимых вычислительных ветвей
            InterOpNumThreads = 1,
             // Включает максимальный уровень оптимизации ONNX-графа
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _session = new InferenceSession(onnxPath, sessionOptions);

        _tokenizer = new XLMRobertaTokenizer(vocabPath, MaxLen);
        _labels = JsonSerializer.Deserialize<LabelsRoot>(File.ReadAllText(labelsPath))!;
        _routingTask = _labels.multi.Keys.Single();
        _singleTasks = _labels.single.Keys.ToList();
    }

    //Токенизирует и классифицирует список текстов одним пакетом
    public List<ClassificationResult> PredictBatch(List<string> texts)
    {
        int batchSize = texts.Count;

        long[] inputIdsFlat = new long[batchSize * MaxLen];
        long[] attentionMaskFlat = new long[batchSize * MaxLen];


        Parallel.For(0, batchSize, i =>
        {
            var (ids, mask) = _tokenizer.Tokenize(texts[i]);
            for (int j = 0; j < MaxLen; j++)
            {
                inputIdsFlat[i * MaxLen + j] = ids[j];
                attentionMaskFlat[i * MaxLen + j] = mask[j];
            }
        });

        // Создаёт входные тензоры с именами, которые ожидает ONNX-модель
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(
                "input_ids", 
                new DenseTensor<long>(inputIdsFlat, new[] { batchSize, MaxLen })
            ),
            NamedOnnxValue.CreateFromTensor(
                "attention_mask", 
                new DenseTensor<long>(attentionMaskFlat, new[] { batchSize, MaxLen })
            ),
        };

        using var results = _session.Run(inputs);
        var dict = results.ToDictionary(o => o.Name, o => o);

        return DecodeBatch(dict, batchSize);
    }
    // Преобразует выходные тензоры ONNX-модели в результаты для каждого текста
    private List<ClassificationResult> DecodeBatch(IDictionary<string, DisposableNamedOnnxValue> dict, int batchSize)
    {
        float[,] routingLogits = GetBatchLogits(dict[$"{_routingTask}_multi_logits"], batchSize, _labels.multi[_routingTask].Length);
        var singleLogits = new Dictionary<string, float[,]>();

        foreach (var task in _singleTasks)
        {
            singleLogits[task] = GetBatchLogits(
                dict[$"{task}_logits"],
                batchSize,
                _labels.single[task].Length
            );
        }

        var results = new List<ClassificationResult>(batchSize);
        // Обрабатывает выходы модели для каждого текста в пакете
        for (int i = 0; i < batchSize; i++)
        {
            var singleResults = new Dictionary<string, string>();

            foreach (var task in _singleTasks)
            {
                singleResults[task] = DecodeSingle(
                    GetRow(singleLogits[task], i),
                    _labels.single[task]
                );
            }


            results.Add(new ClassificationResult
            {
                Routing = DecodeMulti(
                    GetRow(routingLogits, i),
                    _labels.multi[_routingTask]
                ),

                Services = singleResults["Сервисы(тестовая)"] == "Корпоративная почта" ? "Аккаунт Яндекс" : singleResults["Сервисы(тестовая)"], //Блядский костыль((((
                Type = singleResults["Тип задачи"],
                Priority = singleResults["Приоритеты"]
            });
        }

        return results;
    }

    // Преобразует плоский выходной массив модели в двумерный массив
    private static float[,] GetBatchLogits(DisposableNamedOnnxValue value, int batchSize, int numClasses)
    {
        // Извлекает значения тензора в одномерный массив
        float[] flat = value.AsEnumerable<float>().ToArray();
        float[,] logits = new float[batchSize, numClasses];
        for (int i = 0; i < batchSize; i++)
            for (int j = 0; j < numClasses; j++)
                logits[i, j] = flat[i * numClasses + j];
        return logits;
    }

    private static float[] GetRow(float[,] array, int rowIndex)
    {
        int cols = array.GetLength(1);
        float[] row = new float[cols];
        for (int j = 0; j < cols; j++)
            row[j] = array[rowIndex, j];
        return row;
    }

// Декодирует многометочный выход, например список отделов для маршрутизации
    private static List<string> DecodeMulti(float[] logits, string[] labels)
    {
        var result = new List<string>(labels.Length);
        float maxProb = float.MinValue;
        int maxIdx = 0;

        for (int i = 0; i < labels.Length; i++)
        {
            float prob = Sigmoid(logits[i]);

            // основной порог
            if (prob >= THRESHOLD)
                result.Add(labels[i]);

            // запоминаем максимум
            if (prob > maxProb)
            {
                maxProb = prob;
                maxIdx = i;
            }
        }

        // если ничего не прошло порог — берём лучший
        if (result.Count == 0)
            result.Add(labels[maxIdx]);

        return result;
    }

    // Декодирует однозначный выход: возвращает метку с максимальным logit
    private static string DecodeSingle(float[] logits, string[] labels)
    {
        int idx = Array.IndexOf(logits, logits.Max());
        return labels[idx];
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}