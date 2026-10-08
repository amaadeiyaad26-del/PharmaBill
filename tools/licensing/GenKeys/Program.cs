using System.Security.Cryptography;
var dir = @"c:\Users\newth\PharmaBill\tools\licensing";
Directory.CreateDirectory(dir);
using var rsa = RSA.Create(2048);
File.WriteAllText(Path.Combine(dir, "pharmabill-license-private.pem"), rsa.ExportPkcs8PrivateKeyPem());
var pub = rsa.ExportSubjectPublicKeyInfoPem();
File.WriteAllText(Path.Combine(dir, "pharmabill-license-public.pem"), pub);
Console.WriteLine(pub);
