using Arkko.Core;

namespace Arkko.Tests;

public sealed class SecretPlanTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccs-plan-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 10, 15);

    public void Dispose()
    {
        // Solo la carpeta temporal que ha creado este test.
        if (_dir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Un_token_con_frecuencia_tiene_recordatorio_y_uno_sin_ella_no()
    {
        var token = new SecretCard { Name = "GITEA_TOKEN", Created = Today, RotateEveryDays = 30 };
        var never = new SecretCard { Name = "Sin recordatorio", Created = Today };
        var plan = new SecretPlan { Cards = [token, never] };

        Assert.Equal(new DateOnly(2026, 11, 14), token.Due);
        Assert.Null(never.Due);
        Assert.Equal(0, plan.DueCount(Today));
        Assert.Equal(1, plan.DueCount(new DateOnly(2026, 11, 14)));

        // Cambiado: el recordatorio pasa a la siguiente fecha, contada desde ese día.
        SecretPlan.Rotated(token, new DateOnly(2026, 11, 20));
        Assert.Equal(new DateOnly(2026, 12, 20), token.Due);
    }

    [Fact]
    public void Una_semana_antes_toca_pronto_y_el_dia_toca()
    {
        var token = new SecretCard { Created = Today, RotateEveryDays = 30 };
        var plan = new SecretPlan { Cards = [token] };
        var due = token.Due!.Value;

        Assert.Equal(0, plan.SoonCount(due.AddDays(-SecretPlan.WarnDays - 1)));
        Assert.Equal(1, plan.SoonCount(due.AddDays(-SecretPlan.WarnDays)));
        Assert.Equal((0, 1), (plan.SoonCount(due), plan.DueCount(due)));
    }

    [Fact]
    public void Dejarlo_para_mas_tarde_mueve_el_recordatorio_y_cambiarlo_vuelve_a_la_fecha_normal()
    {
        var card = new SecretCard { Created = Today, RotateEveryDays = 30, StepsDone = [0, 1] };
        var plan = new SecretPlan { Cards = [card] };
        var due = card.Due!.Value;

        SecretPlan.Snooze(card, due, 7);
        Assert.Equal(due.AddDays(7), card.Due);
        Assert.Equal(0, plan.DueCount(due));

        SecretPlan.Rotated(card, due.AddDays(7));
        Assert.Null(card.RemindOn);
        Assert.Empty(card.StepsDone);
        Assert.Equal(due.AddDays(37), card.Due);
    }

    [Fact]
    public void El_recordatorio_se_puede_adelantar_pero_no_a_antes_de_hoy()
    {
        var card = new SecretCard { Created = Today, RotateEveryDays = 30 };
        var normal = card.Due!.Value;

        SecretPlan.MoveTo(card, normal.AddDays(-7), Today);
        Assert.Equal(normal.AddDays(-7), card.Due);

        SecretPlan.MoveTo(card, Today.AddDays(-3), Today);
        Assert.Equal(Today, card.Due);

        // Volver a la fecha normal deja de estar movido.
        SecretPlan.MoveTo(card, normal, Today);
        Assert.Null(card.RemindOn);
    }

    private static SecretGroup Leak(string fingerprint, DateOnly seen) =>
        new(fingerprint, "Token", "ghp_…", [new SecretFinding { Fingerprint = fingerprint, WhenUtc = seen.ToDateTime(new TimeOnly(12, 0)).ToUniversalTime() }]);

    [Fact]
    public void Un_token_se_reconoce_por_el_valor_de_su_variable_y_por_las_huellas_enlazadas()
    {
        var byVariable = new SecretCard { Name = "GITEA_TOKEN", EnvVar = "GITEA_TOKEN" };
        var byHand = new SecretCard { Name = "Otro", Fingerprints = ["fp-otro"] };
        var unknown = new SecretCard { Name = "Sin enlace", EnvVar = "NO_EXISTE" };
        string? Read(string name) => name == "GITEA_TOKEN" ? "valor-actual" : null;
        static string Print(string value) => "fp-" + value;

        var owners = TokenLeaks.Mark([byVariable, byHand, unknown], [Leak("fp-valor-actual", Today), Leak("fp-otro", Today)], Read, Print);

        Assert.True(byVariable.Leaked);
        Assert.True(byHand.Leaked);
        Assert.False(unknown.Leaked);
        Assert.Equal("GITEA_TOKEN", owners["fp-valor-actual"].Name);
        Assert.True(SecretPlan.IsDue(byHand, Today));
    }

    [Fact]
    public void Un_hallazgo_de_antes_del_ultimo_cambio_no_vuelve_a_poner_el_token_en_rojo()
    {
        var card = new SecretCard { Name = "GITEA_TOKEN", Fingerprints = ["fp-viejo"], LastRotated = Today, RotateEveryDays = 90, Created = Today };
        TokenLeaks.Mark([card], [Leak("fp-viejo", Today.AddDays(-2))], _ => null, v => v);
        Assert.False(card.Leaked);
        Assert.False(SecretPlan.IsDue(card, Today));

        // Si vuelve a salir después del cambio, sí.
        TokenLeaks.Mark([card], [Leak("fp-viejo", Today.AddDays(1))], _ => null, v => v);
        Assert.True(card.Leaked);
    }

    [Fact]
    public void Se_guarda_cifrado_y_se_lee_igual()
    {
        var store = new SecretPlanStore(_dir);
        Assert.False(store.Exists);
        Assert.Empty(store.Load().Cards);

        var plan = new SecretPlan
        {
            Cards = [new SecretCard { Name = "PANEL_API_KEY", Use = "Panel de tareas", Created = Today, RotateEveryDays = 90, RotationSteps = ["Crear la clave nueva"],
                EnvVar = "PANEL_API_KEY", Fingerprints = ["abc123"], Leaked = true }],
        };
        store.Save(plan);

        // En disco no se lee nada: ni el nombre del token ni para qué sirve.
        var bytes = File.ReadAllBytes(Path.Combine(_dir, "plan.bin"));
        Assert.DoesNotContain("PANEL_API_KEY", System.Text.Encoding.UTF8.GetString(bytes));

        var read = new SecretPlanStore(_dir).Load();
        Assert.Equal(("PANEL_API_KEY", 90, Today), (read.Cards[0].Name, read.Cards[0].RotateEveryDays, read.Cards[0].Created!.Value));
        Assert.Equal(["Crear la clave nueva"], read.Cards[0].RotationSteps);
        Assert.Equal(("PANEL_API_KEY", true), (read.Cards[0].EnvVar, read.Cards[0].Fingerprints.Contains("abc123")));
        // Que ha salido en Seguridad no se guarda: se vuelve a mirar cada vez.
        Assert.False(read.Cards[0].Leaked);
    }
}
