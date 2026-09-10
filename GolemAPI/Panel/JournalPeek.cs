using System.IO.Hashing;
using System.Text;

namespace GolemAPI.Panel;

// DEBUG-grade reader of one journal record's wire frame, mirrored from the
// engine's BinaryEventCodec (FileSystem backend, phase-4 Action split):
//
//   [len?][type byte][entryId:8][occurredAt:8][actionId:4 (action/define only)]
//   [payloadLen:4][payload utf8][exposeLen:4][expose utf8][crc32:4]
//
// Only plain records are readable (no compression/encryption — which this host
// never configures). Anything that does not parse cleanly comes back false and
// the panel shows a generic row: this peek exists for the operator's eyes, the
// engine remains the only authority on the format.
public static class JournalPeek
{
    private const byte CompressionBit = 0x80;
    private const byte EncryptionBit = 0x40;
    private const byte TypeMask = 0x3F;

    public sealed record Peek(long EntryId, string Kind, int ActionId, string Text, string ExposeData);

    public static bool TryDescribe(byte[] raw, out Peek peek)
    {
        peek = null;
        try
        {
            if (raw == null || raw.Length < 22) return false;

            // The frame may arrive with its 4-byte length prefix still on.
            int start = 0, length = raw.Length;
            int prefixed = BitConverter.ToInt32(raw, 0);
            if (prefixed == raw.Length - 4) { start = 4; length = prefixed; }

            int crcOffset = start + length - 4;
            uint stored = BitConverter.ToUInt32(raw, crcOffset);
            var crc = new Crc32();
            crc.Append(raw.AsSpan(start, crcOffset - start));
            if (stored != BitConverter.ToUInt32(crc.GetCurrentHash(), 0)) return false;

            int offset = start;
            byte typeByte = raw[offset++];
            if ((typeByte & (CompressionBit | EncryptionBit)) != 0) return false; // not plain — not ours to read
            int kind = typeByte & TypeMask;

            long entryId = BitConverter.ToInt64(raw, offset); offset += 8;
            offset += 8; // occurredAt

            int actionId = 0;
            if (kind is 1 or 2) { actionId = BitConverter.ToInt32(raw, offset); offset += 4; }

            int payloadLen = BitConverter.ToInt32(raw, offset); offset += 4;
            if (offset + payloadLen > crcOffset) return false;
            string text = Encoding.UTF8.GetString(raw, offset, payloadLen);
            offset += payloadLen;

            string expose = null;
            if (offset + 4 <= crcOffset)
            {
                int exposeLen = BitConverter.ToInt32(raw, offset); offset += 4;
                if (exposeLen > 0 && offset + exposeLen <= crcOffset)
                    expose = Encoding.UTF8.GetString(raw, offset, exposeLen);
            }

            peek = new Peek(entryId,
                kind switch { 0 => "script", 1 => "action", 2 => "define", _ => "record" },
                actionId, text, expose);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // One readable line out of a DSL text: collapse whitespace, cap the length.
    public static string OneLine(string text, int max = 160)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string flat = string.Join(' ',
            text.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
