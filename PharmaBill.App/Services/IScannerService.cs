using System;

namespace PharmaBill.App.Services;

public interface IScannerService
{
	event EventHandler<string>? BarcodeReceived;

	bool IsListening { get; }

	string Status { get; }

	void Start();

	void Stop();

	void Toggle();
}
