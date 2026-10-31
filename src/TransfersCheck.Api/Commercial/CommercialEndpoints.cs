using Microsoft.Extensions.Options;
using TransfersCheck.Core;
namespace TransfersCheck.Api.Commercial;
public static class CommercialEndpoints
{
    private sealed record ContactBody(string? Name, string? Company, string? Phone, string? Email, string? Reason);
    public static void Map(WebApplication app) => app.MapPost("/api/commercial/contact", Submit);
    private static async Task<IResult> Submit(ContactBody body, IMailSender mail, IOptions<OwnerOptions> owner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Company)) return Results.BadRequest(new { error = "required_fields" });
        if (string.IsNullOrWhiteSpace(owner.Value.Email)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        var text = $"Nueva consulta comercial\n\nNombre: {body.Name}\nEmpresa / Software: {body.Company}\nTeléfono: {body.Phone}\nEmail: {body.Email}\nMotivo: {body.Reason}";
        await mail.SendAsync(owner.Value.Email, $"Consulta comercial TransfersCheck - {body.Reason}", text, ct);
        return Results.Ok(new { received = true });
    }
}
