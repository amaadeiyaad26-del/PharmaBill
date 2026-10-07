using System.CodeDom.Compiler;
using System.Collections;
using System.Runtime.CompilerServices;

namespace System.Text.RegularExpressions.Generated;

[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
internal sealed class _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DoctorRegex_3 : Regex
{
	private sealed class RunnerFactory : RegexRunnerFactory
	{
		private sealed class Runner : RegexRunner
		{
			protected override void Scan(ReadOnlySpan<char> inputSpan)
			{
				while (TryFindNextPossibleStartingPosition(inputSpan) && !TryMatchAtCurrentPosition(inputSpan) && runtextpos != inputSpan.Length)
				{
					runtextpos++;
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
				}
			}

			private bool TryFindNextPossibleStartingPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				if (num <= inputSpan.Length - 4)
				{
					int num2 = inputSpan.Slice(num).IndexOfAny(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_indexOfString_dr_OrdinalIgnoreCase);
					if (num2 >= 0)
					{
						runtextpos = num + num2;
						return true;
					}
				}
				runtextpos = inputSpan.Length;
				return false;
			}

			private bool TryMatchAtCurrentPosition(ReadOnlySpan<char> inputSpan)
			{
				int num = runtextpos;
				int start = num;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				int num5 = 0;
				int num6 = 0;
				int pos = 0;
				int num7 = 0;
				ReadOnlySpan<char> span = inputSpan.Slice(num);
				if (!_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.IsPreWordCharBoundary(inputSpan, num))
				{
					UncaptureUntil(0);
					return false;
				}
				if ((uint)span.Length < 2u || !span.StartsWith("dr", StringComparison.OrdinalIgnoreCase))
				{
					UncaptureUntil(0);
					return false;
				}
				if ((uint)span.Length > 2u && span[2] == '.')
				{
					span = span.Slice(1);
					num++;
				}
				int num8 = span.Slice(2).IndexOfAnyExcept('\t', ' ');
				if (num8 < 0)
				{
					num8 = span.Length - 2;
				}
				if (num8 == 0)
				{
					UncaptureUntil(0);
					return false;
				}
				span = span.Slice(num8);
				num += num8;
				num += 2;
				span = inputSpan.Slice(num);
				num2 = num;
				char c;
				if (span.IsEmpty || (((c = span[0]) < '\u0080') ? (!char.IsAsciiLetter(c)) : (!RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ"))))
				{
					UncaptureUntil(0);
					return false;
				}
				num++;
				span = inputSpan.Slice(num);
				num4 = num;
				int num9 = span.IndexOfAnyExcept(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1);
				if (num9 < 0)
				{
					num9 = span.Length;
				}
				span = span.Slice(num9);
				num += num9;
				num5 = num;
				while (true)
				{
					num3 = Crawlpos();
					num7 = pos;
					num6 = 0;
					while (true)
					{
						_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.StackPush(ref runstack, ref pos, Crawlpos(), num);
						num6++;
						int num10 = span.IndexOfAnyExcept('\t', ' ');
						if (num10 < 0)
						{
							num10 = span.Length;
						}
						if (num10 != 0)
						{
							span = span.Slice(num10);
							num += num10;
							if (!span.IsEmpty && !(((c = span[0]) < '\u0080') ? (!char.IsAsciiLetter(c)) : (!RegexRunner.CharInClass(c, "\0\u0006\0A[a{KÅ"))))
							{
								int num11 = span.Slice(1).IndexOfAnyExcept(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_nonAscii_01FE667C4BB729047C81DE888AA0FBC694D1DFE7FE21E6740BEFF0BFCE194DC1);
								if (num11 < 0)
								{
									num11 = span.Length - 1;
								}
								span = span.Slice(num11);
								num += num11;
								num++;
								span = inputSpan.Slice(num);
								if (num6 < 3)
								{
									continue;
								}
								goto IL_02a3;
							}
						}
						if (--num6 < 0)
						{
							break;
						}
						num = runstack[--pos];
						UncaptureUntil(runstack[--pos]);
						span = inputSpan.Slice(num);
						goto IL_02a3;
						IL_02a3:
						pos = num7;
						Capture(1, num2, num);
						runtextpos = num;
						Capture(0, start, num);
						return true;
					}
					UncaptureUntil(num3);
					if (_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_hasTimeout)
					{
						CheckTimeout();
					}
					if (num4 >= num5)
					{
						break;
					}
					num = --num5;
					span = inputSpan.Slice(num);
				}
				UncaptureUntil(0);
				return false;
				[MethodImpl(MethodImplOptions.AggressiveInlining)]
				void UncaptureUntil(int capturePosition)
				{
					while (Crawlpos() > capturePosition)
					{
						Uncapture();
					}
				}
			}
		}

		protected override RegexRunner CreateInstance()
		{
			return new Runner();
		}
	}

	internal static readonly _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DoctorRegex_3 Instance = new _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DoctorRegex_3();

	private _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__DoctorRegex_3()
	{
		pattern = "\\bDr\\.?[ \\t]+(?<n>[A-Za-z][A-Za-z.'\\-]*(?:[ \\t]+[A-Za-z][A-Za-z.'\\-]*){0,3})";
		roptions = RegexOptions.IgnoreCase;
		Regex.ValidateMatchTimeout(_003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout);
		internalMatchTimeout = _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__Utilities.s_defaultTimeout;
		factory = new RunnerFactory();
		CapNames = new Hashtable
		{
			{ "0", 0 },
			{ "n", 1 }
		};
		capslist = new string[2] { "0", "n" };
		capsize = 2;
	}
}
