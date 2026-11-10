using Tokenizers.HuggingFace.Tokenizer;

public class XLMRobertaTokenizer
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxLen;

    public XLMRobertaTokenizer(string tokenizerPath, int maxLen = 256)
    {
        _tokenizer = Tokenizer.FromFile(tokenizerPath);
        _maxLen = maxLen;
    }

    public (long[] inputIds, long[] attentionMask) Tokenize(string text)
    {
        var encoding = _tokenizer.Encode(text, addSpecialTokens: true);

        var ids = encoding
            .SelectMany(x => x.Ids)
            .Select(x => (long)x)
            .ToList();

        var mask = Enumerable.Repeat(1L, ids.Count).ToList();

        if (ids.Count > _maxLen) //обрезание
        {
            ids = ids.Take(_maxLen).ToList();
            mask = mask.Take(_maxLen).ToList();
        }

        while (ids.Count < _maxLen)
        {
            ids.Add(1); 
            mask.Add(0);
        }

        return (ids.ToArray(), mask.ToArray());
    }
}