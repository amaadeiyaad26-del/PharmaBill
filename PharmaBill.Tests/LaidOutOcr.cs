using PharmaBill.Core.Ai;

namespace PharmaBill.Tests;

/// <summary>
/// Stands in for Windows OCR on a photo of a bill: the text as written, and every word with its position on the page,
/// as the real recognizer gives them. Each character is 7 px wide and each line 20 px below the last, so a bill typed
/// with its columns lined up reads as a photo of that bill would.
/// </summary>
internal sealed class LaidOutOcr(string text) : IOcrTextRecognizer
{
    public Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) => Task.FromResult(text);

    public Task<IReadOnlyList<OcrWordPosition>> RecognizeWordsAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        List<OcrWordPosition> words = new List<OcrWordPosition>();
        string[] lines = text.Replace("\r", string.Empty).Split('\n');
        for (int row = 0; row < lines.Length; row++)
        {
            string line = lines[row];
            int column = 0;
            while (column < line.Length)
            {
                if (line[column] == ' ')
                {
                    column++;
                    continue;
                }
                int start = column;
                while (column < line.Length && line[column] != ' ')
                {
                    column++;
                }
                words.Add(new OcrWordPosition(line[start..column], start * 7, row * 20, (column - start) * 7, 12));
            }
        }
        return Task.FromResult<IReadOnlyList<OcrWordPosition>>(words);
    }
}
