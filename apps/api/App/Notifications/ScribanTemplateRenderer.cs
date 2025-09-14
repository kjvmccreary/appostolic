using Appostolic.Api.App.Options;
using Scriban;
using Scriban.Runtime;

namespace Appostolic.Api.App.Notifications;

public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly EmailOptions _emailOptions;

    public ScribanTemplateRenderer(Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions)
    {
        _emailOptions = emailOptions.Value;
    }

    public Task<(string Subject, string Html, string Text)> RenderAsync(EmailMessage msg, CancellationToken ct)
    {
        // Merge data with standard fields
        var model = new Dictionary<string, object?>(msg.Data)
        {
            ["webBaseUrl"] = _emailOptions.WebBaseUrl,
            ["toName"] = msg.ToName,
        };

        var (subjectTpl, htmlTpl, textTpl) = msg.Kind switch
        {
            EmailKind.Verification => (
                "Verify your email",
                "<p>Hello {{ toName ?? 'there' }},</p><p>Please verify your email by clicking <a href='{{ link }}'>this link</a>.</p><p>If you didn't request this, you can ignore this email.</p>",
                "Hello {{ toName ?? 'there' }},\n\nVerify your email: {{ link }}\n\nIf you didn't request this, you can ignore this email."
            ),
            EmailKind.Invite => (
                "You're invited to join {{ tenant }}",
                "<p>Hello,</p><p>You were invited to join <b>{{ tenant }}</b> as <b>{{ role }}</b>. Click <a href='{{ link }}'>Accept invite</a> to continue.</p><p>Invited by: {{ inviter }}</p>",
                "You were invited to join {{ tenant }} as {{ role }}. Accept: {{ link }} (Invited by: {{ inviter }})"
            ),
            EmailKind.MagicLink => (
                "Sign in to Appostolic",
                "<p>Hello {{ toName ?? 'there' }},</p><p>Use your magic link to sign in: <a href='{{ link }}'>Sign in</a>.</p><p>This link expires in 15 minutes. If you didn't request it, you can ignore this email.</p>",
                "Hello {{ toName ?? 'there' }},\n\nSign in with your magic link: {{ link }}\n\nThis link expires in 15 minutes. If you didn't request it, you can ignore this email."
            ),
            _ => ("Notification", "<p>Notification</p>", "Notification")
        };

        // Render with Scriban using a ScriptObject model
        string RenderTpl(string tpl)
        {
            var script = new ScriptObject();
            foreach (var kv in model)
            {
                script.SetValue(kv.Key, kv.Value, true);
            }
            var ctx = new TemplateContext();
            ctx.PushGlobal(script);
            var template = Template.Parse(tpl);
            return template.Render(ctx);
        }

        var subject = RenderTpl(subjectTpl);
        var html = RenderTpl(htmlTpl);
        var text = RenderTpl(textTpl);
        return Task.FromResult((subject, html, text));
    }
}
