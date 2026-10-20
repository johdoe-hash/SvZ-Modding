using System.Security.Cryptography;
using System.Text;

// Reads and writes Samurai vs Zombies save files (save.data), which the game stores as:
//   16-byte IV, 16-byte salt, then AES-256-CBC (key from the game's password + salt) of
//   [int64 length][save text][SHA-256 of the save text]
// Usage: SaveTool decrypt <save.data> <out.txt>
//        SaveTool encrypt <in.txt> <save.data>
const string Password = "_Glu#Games$2012_";

if (args.Length != 3 || (args[0] != "decrypt" && args[0] != "encrypt"))
{
	Console.Error.WriteLine("usage: SaveTool decrypt <save.data> <out.txt> | encrypt <in.txt> <save.data>");
	return 1;
}
if (args[0] == "decrypt")
{
	File.WriteAllText(args[2], Decrypt(File.ReadAllBytes(args[1])));
}
else
{
	File.WriteAllBytes(args[2], Encrypt(File.ReadAllText(args[1])));
	if (Decrypt(File.ReadAllBytes(args[2])) != File.ReadAllText(args[1]))
	{
		throw new InvalidOperationException("round trip failed");
	}
}
Console.WriteLine("ok");
return 0;

static Aes Cipher(byte[] salt, byte[] iv)
{
#pragma warning disable SYSLIB0041 // the game derives its key with the legacy PasswordDeriveBytes
	var derive = new PasswordDeriveBytes(Password, salt, "SHA256", 1000);
	var aes = Aes.Create();
	aes.KeySize = 256;
	aes.Key = derive.GetBytes(32);
#pragma warning restore SYSLIB0041
	aes.IV = iv;
	aes.Padding = PaddingMode.PKCS7;
	return aes;
}

static string Decrypt(byte[] file)
{
	using Aes aes = Cipher(file[16..32], file[..16]);
	byte[] plain = aes.DecryptCbc(file[32..], aes.IV);
	long length = BitConverter.ToInt64(plain, 0);
	byte[] text = plain[8..(8 + (int)length)];
	byte[] hash = plain[(8 + (int)length)..(8 + (int)length + 32)];
	if (!SHA256.HashData(text).SequenceEqual(hash))
	{
		throw new InvalidDataException("save hash doesn't match");
	}
	return Encoding.ASCII.GetString(text);
}

static byte[] Encrypt(string saveText)
{
	byte[] text = Encoding.ASCII.GetBytes(saveText);
	byte[] iv = RandomNumberGenerator.GetBytes(16);
	byte[] salt = RandomNumberGenerator.GetBytes(16);
	using Aes aes = Cipher(salt, iv);
	byte[] plain = [.. BitConverter.GetBytes((long)text.Length), .. text, .. SHA256.HashData(text)];
	return [.. iv, .. salt, .. aes.EncryptCbc(plain, iv)];
}
