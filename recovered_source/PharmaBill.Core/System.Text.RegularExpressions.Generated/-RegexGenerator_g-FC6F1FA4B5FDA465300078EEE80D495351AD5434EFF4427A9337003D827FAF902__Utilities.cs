using System.Buffers;
using System.CodeDom.Compiler;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal static class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities
{
	internal static readonly TimeSpan s_defaultTimeout = ((AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT") is TimeSpan timeSpan) ? timeSpan : Regex.InfiniteMatchTimeout);

	internal static readonly bool s_hasTimeout = s_defaultTimeout != Regex.InfiniteMatchTimeout;

	private const int WordCategoriesMask = 262463;

	internal static readonly SearchValues<char> s_asciiExceptDigits = SearchValues.Create("\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f !\"#$%&'()*+,-./:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u007f");

	internal static readonly SearchValues<char> s_ascii_100FF0300000000FEFFFF07 = SearchValues.Create(" 0123456789abcdefghijklmnopqrstuvwxyz");

	internal static readonly SearchValues<char> s_ascii_1F000000000000000000000 = SearchValues.Create(" ,-./");

	internal static readonly SearchValues<char> s_ascii_2040FF03FEFFFF07FEFFFF07 = SearchValues.Create("%.0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

	internal static readonly SearchValues<char> s_ascii_FFFFFFFFFFFF00FCBFFFFFFEBFFFFFFE = SearchValues.Create("\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f !\"#$%&'()*+,-./:;<=>?@ABCDEGHIJKLMNOPQRSTUVWYZ[\\]^_`abcdeghijklmnopqrstuvwyz{|}~\u007f");

	internal static readonly SearchValues<char> s_ascii_FFFFFFFFFFFF00FCFB7EE5FFFB7EE5FF = SearchValues.Create("\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f !\"#$%&'()*+,-./:;<=>?@ACDEFGIJKLMNPRUVWXYZ[\\]^_`acdefgijklmnpruvwxyz{|}~\u007f");

	internal static readonly SearchValues<string> s_indexOfAnyStrings_OrdinalIgnoreCase_9524FCE8E4AC33AF9A2FA6F2B7ACD0D708966E284FA44062557B6F0A8B966F45;

	internal static readonly SearchValues<string> s_indexOfAnyStrings_OrdinalIgnoreCase_95F97D95F39C0977E19DF37421C4FA89F00399940FCACC8AC12D7B6429EA66D6;

	internal static readonly SearchValues<string> s_indexOfAnyStrings_OrdinalIgnoreCase_BAB603892FCAD558374C9BEC405360D05277BA19FBF0D0413CC6FFE09B2E7F49;

	internal static readonly SearchValues<string> s_indexOfString_dr_OrdinalIgnoreCase;

	internal static readonly SearchValues<string> s_indexOfString_reg_OrdinalIgnoreCase;

	internal static readonly SearchValues<char> s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1;

	internal static readonly SearchValues<char> s_whitespace;

	private static ReadOnlySpan<byte> WordCharBitmap => new byte[16]
	{
		0, 0, 0, 0, 0, 0, 255, 3, 254, 255,
		255, 135, 254, 255, 255, 7
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IndexOfAnyDigit(this ReadOnlySpan<char> span)
	{
		int num = span.IndexOfAnyExcept(s_asciiExceptDigits);
		if ((uint)num < (uint)span.Length)
		{
			if (char.IsAscii(span[num]))
			{
				return num;
			}
			do
			{
				if (char.IsDigit(span[num]))
				{
					return num;
				}
				num++;
			}
			while ((uint)num < (uint)span.Length);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IndexOfNonAsciiOrAny_22F379E94C9DFDFD07A5ECBCA0459F0F6BFC8B0386C0D244B4AE253FBF2DC6B6(this ReadOnlySpan<char> span)
	{
		int num = span.IndexOfAnyExcept(s_ascii_FFFFFFFFFFFF00FCFB7EE5FFFB7EE5FF);
		if ((uint)num < (uint)span.Length)
		{
			if (char.IsAscii(span[num]))
			{
				return num;
			}
			do
			{
				char c;
				if (((c = span[num]) < '\u0080') ? (("\0\0\0Ͽ脄\u001a脄\u001a"[(int)c >> 4] & (1 << (c & 0xF))) != 0) : RegexRunner.CharInClass(c, "\0\u0014\u0002BCHIOPQRSUbchiopqrsu\t\t"))
				{
					return num;
				}
				num++;
			}
			while ((uint)num < (uint)span.Length);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IndexOfNonAsciiOrAny_993E894700B1E930D7454E263639B6F6576AC6101B98AE409E33EEFAB3BE1D36(this ReadOnlySpan<char> span)
	{
		int num = span.IndexOfAnyExcept(s_ascii_FFFFFFFFFFFF00FCBFFFFFFEBFFFFFFE);
		if ((uint)num < (uint)span.Length)
		{
			if (char.IsAscii(span[num]))
			{
				return num;
			}
			do
			{
				char c;
				if (((c = span[num]) < '\u0080') ? (("\0\0\0Ͽ@Ā@Ā"[(int)c >> 4] & (1 << (c & 0xF))) != 0) : RegexRunner.CharInClass(c, "\0\b\u0001FGXYfgxy\t"))
				{
					return num;
				}
				num++;
			}
			while ((uint)num < (uint)span.Length);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IndexOfNonAsciiOrAny_BFFE8177F92F37F4E241C99748DFE317D23275CB27C8199145CC6E82FE86EF5C(this ReadOnlySpan<char> span)
	{
		int num = span.IndexOfAnyExcept(s_ascii_2040FF03FEFFFF07FEFFFF07);
		if ((uint)num < (uint)span.Length)
		{
			if (char.IsAscii(span[num]))
			{
				return num;
			}
			do
			{
				char c;
				if (((c = span[num]) < '\u0080') ? (("\uffff\uffff뿟ﰀ\u0001\uf800\u0001\uf800"[(int)c >> 4] & (1 << (c & 0xF))) != 0) : RegexRunner.CharInClass(c, "\u0001\u0004\f%&./\0\u0002\u0004\u0005\u0003\u0001\0\0\t\n\v\0"))
				{
					return num;
				}
				num++;
			}
			while ((uint)num < (uint)span.Length);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsBoundary(ReadOnlySpan<char> inputSpan, int index)
	{
		int num = index - 1;
		return ((uint)num < (uint)inputSpan.Length && IsBoundaryWordChar(inputSpan[num])) != ((uint)index < (uint)inputSpan.Length && IsBoundaryWordChar(inputSpan[index]));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsBoundaryWordChar(char ch)
	{
		ReadOnlySpan<byte> wordCharBitmap = WordCharBitmap;
		int num = (int)ch >> 3;
		if ((uint)num < (uint)wordCharBitmap.Length)
		{
			return (wordCharBitmap[num] & (1 << (ch & 7))) != 0;
		}
		bool flag = (0x4013F & (1 << (int)CharUnicodeInfo.GetUnicodeCategory(ch))) != 0;
		if (!flag)
		{
			bool flag2 = ((ch == '\u200c' || ch == '\u200d') ? true : false);
			flag = flag2;
		}
		return flag;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsPostWordCharBoundary(ReadOnlySpan<char> inputSpan, int index)
	{
		if ((uint)index < (uint)inputSpan.Length)
		{
			return !IsBoundaryWordChar(inputSpan[index]);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static bool IsPreWordCharBoundary(ReadOnlySpan<char> inputSpan, int index)
	{
		int num = index - 1;
		if ((uint)num < (uint)inputSpan.Length)
		{
			return !IsBoundaryWordChar(inputSpan[num]);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void StackPop(int[] stack, ref int pos, out int arg0, out int arg1)
	{
		arg0 = stack[--pos];
		arg1 = stack[--pos];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void StackPush(ref int[] stack, ref int pos, int arg0)
	{
		int[] array = stack;
		int num = pos;
		if ((uint)num < (uint)array.Length)
		{
			array[num] = arg0;
			pos++;
		}
		else
		{
			WithResize(ref stack, ref pos, arg0);
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void WithResize(ref int[] reference, ref int reference2, int arg1)
		{
			Array.Resize(ref reference, reference2 * 2);
			StackPush(ref reference, ref reference2, arg1);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void StackPush(ref int[] stack, ref int pos, int arg0, int arg1)
	{
		int[] array = stack;
		int num = pos;
		if ((uint)(num + 1) < (uint)array.Length)
		{
			array[num] = arg0;
			array[num + 1] = arg1;
			pos += 2;
		}
		else
		{
			WithResize(ref stack, ref pos, arg0, arg1);
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void WithResize(ref int[] reference, ref int reference2, int arg2, int arg3)
		{
			Array.Resize(ref reference, (reference2 + 1) * 2);
			StackPush(ref reference, ref reference2, arg2, arg3);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void StackPush(ref int[] stack, ref int pos, int arg0, int arg1, int arg2)
	{
		int[] array = stack;
		int num = pos;
		if ((uint)(num + 2) < (uint)array.Length)
		{
			array[num] = arg0;
			array[num + 1] = arg1;
			array[num + 2] = arg2;
			pos += 3;
		}
		else
		{
			WithResize(ref stack, ref pos, arg0, arg1, arg2);
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		static void WithResize(ref int[] reference, ref int reference2, int arg3, int arg4, int arg5)
		{
			Array.Resize(ref reference, (reference2 + 2) * 2);
			StackPush(ref reference, ref reference2, arg3, arg4, arg5);
		}
	}

	static _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities()
	{
		InlineArray2<string> buffer = default;
		buffer[0] = "date";
		buffer[1] = "dt";
		s_indexOfAnyStrings_OrdinalIgnoreCase_9524FCE8E4AC33AF9A2FA6F2B7ACD0D708966E284FA44062557B6F0A8B966F45 = SearchValues.Create(buffer, StringComparison.OrdinalIgnoreCase);
		InlineArray14<string> buffer2 = default;
		buffer2[0] = "dr";
		buffer2[1] = "reg";
		buffer2[2] = "regd";
		buffer2[3] = "registrati";
		buffer2[4] = "patient";
		buffer2[5] = "date";
		buffer2[6] = "phone";
		buffer2[7] = "mob";
		buffer2[8] = "mobile";
		buffer2[9] = "address";
		buffer2[10] = "clinic";
		buffer2[11] = "hospital";
		buffer2[12] = "age";
		buffer2[13] = "name";
		s_indexOfAnyStrings_OrdinalIgnoreCase_95F97D95F39C0977E19DF37421C4FA89F00399940FCACC8AC12D7B6429EA66D6 = SearchValues.Create(buffer2, StringComparison.OrdinalIgnoreCase);
		InlineArray5<string> buffer3 = default;
		buffer3[0] = "pat";
		buffer3[1] = "pati";
		buffer3[2] = "pate";
		buffer3[3] = "pt";
		buffer3[4] = "name";
		s_indexOfAnyStrings_OrdinalIgnoreCase_BAB603892FCAD558374C9BEC405360D05277BA19FBF0D0413CC6FFE09B2E7F49 = SearchValues.Create(buffer3, StringComparison.OrdinalIgnoreCase);
		s_indexOfString_dr_OrdinalIgnoreCase = SearchValues.Create(new ReadOnlySpan<string>("dr"), StringComparison.OrdinalIgnoreCase);
		s_indexOfString_reg_OrdinalIgnoreCase = SearchValues.Create(new ReadOnlySpan<string>("reg"), StringComparison.OrdinalIgnoreCase);
		s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1 = SearchValues.Create("'-.ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzK");
		s_whitespace = SearchValues.Create("\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000");
	}
}
