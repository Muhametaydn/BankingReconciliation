using System.Security.Cryptography;
using System.Text;
using BankingReconciliation.Api.Models;

namespace BankingReconciliation.Api.Services;

public static class ReconciliationAuditIntegrity
{
    public static string ComputeHash(ReconciliationAuditEvent auditEvent)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Write(writer, auditEvent.Id.ToString("N"));
            Write(writer, auditEvent.CreatedAt.ToUniversalTime().ToString("O"));
            Write(writer, auditEvent.Actor);
            Write(writer, auditEvent.Action.ToString());
            Write(writer, auditEvent.ResourceType.ToString());
            Write(writer, auditEvent.ResourceId);
            Write(writer, auditEvent.BeforeStateJson);
            Write(writer, auditEvent.AfterStateJson);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void Write(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(-1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
