using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;

namespace Pixelbadger.Toolkit.Components;

public class ImageSteganography
{
    private const string MessageTerminator = "<<END>>";
    
    public async Task EncodeMessageAsync(string imagePath, string message, string outputPath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }
        
        var messageWithTerminator = message + MessageTerminator;
        var messageBytes = Encoding.UTF8.GetBytes(messageWithTerminator);
        var messageBits = ConvertToBits(messageBytes);
        
        using var image = await Image.LoadAsync<Rgba32>(imagePath);
        
        if (messageBits.Length > image.Width * image.Height * 3)
        {
            throw new InvalidOperationException("Message too long for the image capacity");
        }
        
        int bitIndex = 0;
        bool encodingComplete = false;
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !encodingComplete; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                
                for (int x = 0; x < pixelRow.Length && !encodingComplete; x++)
                {
                    ref var pixel = ref pixelRow[x];
                    
                    var r = pixel.R;
                    var g = pixel.G;
                    var b = pixel.B;
                    
                    if (bitIndex < messageBits.Length)
                    {
                        r = (byte)((r & 0xFE) | messageBits[bitIndex++]);
                    }
                    
                    if (bitIndex < messageBits.Length)
                    {
                        g = (byte)((g & 0xFE) | messageBits[bitIndex++]);
                    }
                    
                    if (bitIndex < messageBits.Length)
                    {
                        b = (byte)((b & 0xFE) | messageBits[bitIndex++]);
                    }
                    else
                    {
                        encodingComplete = true;
                    }
                    
                    pixel.R = r;
                    pixel.G = g;
                    pixel.B = b;
                }
            }
        });
        
        await image.SaveAsPngAsync(outputPath);
    }
    
    public async Task<string> DecodeMessageAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }
        
        using var image = await Image.LoadAsync<Rgba32>(imagePath);
        var extractedBits = new List<byte>();
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    ref var pixel = ref pixelRow[x];
                    
                    extractedBits.Add((byte)(pixel.R & 1));
                    extractedBits.Add((byte)(pixel.G & 1));
                    extractedBits.Add((byte)(pixel.B & 1));
                }
            }
        });
        
        var messageBytes = ConvertBitsToBytes(extractedBits.ToArray());
        var messageString = Encoding.UTF8.GetString(messageBytes);
        
        var terminatorIndex = messageString.IndexOf(MessageTerminator, StringComparison.Ordinal);
        if (terminatorIndex == -1)
        {
            throw new InvalidOperationException("No valid steganographic message found in the image");
        }
        
        return messageString.Substring(0, terminatorIndex);
    }
    
    private byte[] ConvertToBits(byte[] bytes)
    {
        var bits = new List<byte>();
        
        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                bits.Add((byte)((b >> i) & 1));
            }
        }
        
        return bits.ToArray();
    }
    
    private byte[] ConvertBitsToBytes(byte[] bits)
    {
        var bytes = new List<byte>();
        
        for (int i = 0; i < bits.Length; i += 8)
        {
            if (i + 7 >= bits.Length) break;
            
            byte value = 0;
            for (int j = 0; j < 8; j++)
            {
                value = (byte)((value << 1) | bits[i + j]);
            }
            bytes.Add(value);
        }
        
        return bytes.ToArray();
    }
}