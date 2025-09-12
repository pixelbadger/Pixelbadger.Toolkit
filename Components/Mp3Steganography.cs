using System.Text;

namespace Pixelbadger.Toolkit.Components;

public class Mp3Steganography
{
    private const string MessageTerminator = "<<END>>";
    private const string HiddenDataKey = "TXXX";
    private const string HiddenDataDescription = "STEGO";
    
    public async Task EncodeMessageAsync(string mp3Path, string message, string outputPath)
    {
        if (!File.Exists(mp3Path))
        {
            throw new FileNotFoundException($"MP3 file not found: {mp3Path}");
        }
        
        var mp3Data = await File.ReadAllBytesAsync(mp3Path);
        var messageWithTerminator = message + MessageTerminator;
        var encodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(messageWithTerminator));
        
        var modifiedMp3 = AddOrUpdateId3Tag(mp3Data, encodedData);
        await File.WriteAllBytesAsync(outputPath, modifiedMp3);
    }
    
    public async Task<string> DecodeMessageAsync(string mp3Path)
    {
        if (!File.Exists(mp3Path))
        {
            throw new FileNotFoundException($"MP3 file not found: {mp3Path}");
        }
        
        var mp3Data = await File.ReadAllBytesAsync(mp3Path);
        var encodedMessage = ExtractFromId3Tag(mp3Data);
        
        if (string.IsNullOrEmpty(encodedMessage))
        {
            throw new InvalidOperationException("No hidden message found in MP3 file");
        }
        
        var messageBytes = Convert.FromBase64String(encodedMessage);
        var messageString = Encoding.UTF8.GetString(messageBytes);
        
        var terminatorIndex = messageString.IndexOf(MessageTerminator, StringComparison.Ordinal);
        if (terminatorIndex == -1)
        {
            throw new InvalidOperationException("Invalid hidden message format");
        }
        
        return messageString.Substring(0, terminatorIndex);
    }
    
    private byte[] AddOrUpdateId3Tag(byte[] mp3Data, string encodedMessage)
    {
        var id3StartIndex = FindId3v2StartIndex(mp3Data);
        
        if (id3StartIndex == -1)
        {
            return PrependNewId3Tag(mp3Data, encodedMessage);
        }
        
        return UpdateExistingId3Tag(mp3Data, id3StartIndex, encodedMessage);
    }
    
    private int FindId3v2StartIndex(byte[] data)
    {
        if (data.Length < 10) return -1;
        
        if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33)
        {
            return 0;
        }
        
        return -1;
    }
    
    private byte[] PrependNewId3Tag(byte[] mp3Data, string encodedMessage)
    {
        var frameData = CreateTxxxFrame(encodedMessage);
        var tagSize = 10 + frameData.Length;
        var tagSizeBytes = GetSyncSafeSize(frameData.Length);
        
        var id3Tag = new List<byte>();
        id3Tag.AddRange(new byte[] { 0x49, 0x44, 0x33 });
        id3Tag.AddRange(new byte[] { 0x04, 0x00 }); 
        id3Tag.Add(0x00);
        id3Tag.AddRange(tagSizeBytes);
        id3Tag.AddRange(frameData);
        
        var result = new byte[id3Tag.Count + mp3Data.Length];
        id3Tag.CopyTo(result, 0);
        mp3Data.CopyTo(result, id3Tag.Count);
        
        return result;
    }
    
    private byte[] UpdateExistingId3Tag(byte[] mp3Data, int id3StartIndex, string encodedMessage)
    {
        var tagSizeBytes = new byte[4];
        Array.Copy(mp3Data, id3StartIndex + 6, tagSizeBytes, 0, 4);
        var existingTagSize = GetSyncSafeValue(tagSizeBytes);
        var existingTagEndIndex = id3StartIndex + 10 + existingTagSize;
        
        var frameData = CreateTxxxFrame(encodedMessage);
        var newTagSizeBytes = GetSyncSafeSize(frameData.Length);
        
        var result = new List<byte>();
        
        result.AddRange(mp3Data.Take(id3StartIndex + 6));
        result.AddRange(newTagSizeBytes);
        result.AddRange(frameData);
        result.AddRange(mp3Data.Skip(existingTagEndIndex));
        
        return result.ToArray();
    }
    
    private byte[] CreateTxxxFrame(string encodedMessage)
    {
        var frameId = Encoding.ASCII.GetBytes(HiddenDataKey);
        var description = Encoding.UTF8.GetBytes(HiddenDataDescription);
        var messageBytes = Encoding.UTF8.GetBytes(encodedMessage);
        
        var frameContent = new List<byte>();
        frameContent.Add(0x03);
        frameContent.AddRange(description);
        frameContent.Add(0x00);
        frameContent.AddRange(messageBytes);
        
        var frameSizeBytes = GetSyncSafeSize(frameContent.Count);
        var flagsBytes = new byte[] { 0x00, 0x00 };
        
        var frame = new List<byte>();
        frame.AddRange(frameId);
        frame.AddRange(frameSizeBytes);
        frame.AddRange(flagsBytes);
        frame.AddRange(frameContent);
        
        return frame.ToArray();
    }
    
    private string? ExtractFromId3Tag(byte[] mp3Data)
    {
        var id3StartIndex = FindId3v2StartIndex(mp3Data);
        if (id3StartIndex == -1) return null;
        
        var tagSizeBytes = new byte[4];
        Array.Copy(mp3Data, id3StartIndex + 6, tagSizeBytes, 0, 4);
        var tagSize = GetSyncSafeValue(tagSizeBytes);
        
        var frameStartIndex = id3StartIndex + 10;
        var frameEndIndex = id3StartIndex + 10 + tagSize;
        
        var currentIndex = frameStartIndex;
        
        while (currentIndex < frameEndIndex - 10)
        {
            var frameId = Encoding.ASCII.GetString(mp3Data, currentIndex, 4);
            
            if (frameId == HiddenDataKey)
            {
                var frameSizeBytes = new byte[4];
                Array.Copy(mp3Data, currentIndex + 4, frameSizeBytes, 0, 4);
                var frameSize = GetSyncSafeValue(frameSizeBytes);
                
                var frameContentStart = currentIndex + 10;
                var encoding = mp3Data[frameContentStart];
                
                var descriptionEndIndex = frameContentStart + 1;
                while (descriptionEndIndex < frameContentStart + frameSize && mp3Data[descriptionEndIndex] != 0x00)
                {
                    descriptionEndIndex++;
                }
                
                var messageStart = descriptionEndIndex + 1;
                var messageLength = frameSize - (messageStart - frameContentStart);
                
                if (messageLength > 0)
                {
                    return Encoding.UTF8.GetString(mp3Data, messageStart, messageLength);
                }
            }
            
            if (frameId == "\0\0\0\0") break;
            
            var nextFrameSizeBytes = new byte[4];
            Array.Copy(mp3Data, currentIndex + 4, nextFrameSizeBytes, 0, 4);
            var nextFrameSize = GetSyncSafeValue(nextFrameSizeBytes);
            currentIndex += 10 + nextFrameSize;
        }
        
        return null;
    }
    
    private byte[] GetSyncSafeSize(int size)
    {
        return new byte[]
        {
            (byte)((size >> 21) & 0x7F),
            (byte)((size >> 14) & 0x7F),
            (byte)((size >> 7) & 0x7F),
            (byte)(size & 0x7F)
        };
    }
    
    private int GetSyncSafeValue(byte[] bytes)
    {
        return (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];
    }
}