using System.Collections.Generic;
using System.Text;

namespace PharmaBill.App.Services;

public static class EscPosReceiptBuilder
{
	public static byte[] Build(string receiptText, string qrPayload, bool pulseCashDrawer)
	{
		List<byte> list = new List<byte> { 27, 64 };
		list.AddRange(Encoding.ASCII.GetBytes(receiptText));
		list.AddRange(BuildQrCode(qrPayload));
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[5] { 10, 10, 29, 86, 0 }));
		if (pulseCashDrawer)
		{
			list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[5] { 27, 112, 0, 25, 250 }));
		}
		return list.ToArray();
	}

	private static byte[] BuildQrCode(string content)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(content);
		int num = bytes.Length + 3;
		byte b = (byte)(num % 256);
		byte b2 = (byte)(num / 256);
		List<byte> list = new List<byte>();
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[9] { 29, 40, 107, 4, 0, 49, 65, 50, 0 }));
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[8] { 29, 40, 107, 3, 0, 49, 67, 4 }));
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[8] { 29, 40, 107, 3, 0, 49, 69, 49 }));
		byte[] array = new byte[8] { 29, 40, 107, 0, 0, 49, 80, 48 };
		array[3] = b;
		array[4] = b2;
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(array));
		list.AddRange(bytes);
		list.AddRange(new _003C_003Ez__ReadOnlyArray<byte>(new byte[8] { 29, 40, 107, 3, 0, 49, 81, 48 }));
		return list.ToArray();
	}
}
