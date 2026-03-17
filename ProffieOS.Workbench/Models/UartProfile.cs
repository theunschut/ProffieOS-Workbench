namespace ProffieOS.Workbench.Models;

public record UartProfile(
    string ServiceUuid,
    string RxUuid,
    string TxUuid,
    string? PwUuid = null,
    string? StatusUuid = null
);
