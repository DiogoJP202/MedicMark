using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChecklistPlantao.Server.Controllers;

/// <summary>
/// Política pública exigida pelas lojas. Ela fica no próprio servidor para não depender de um
/// domínio separado e permanece acessível sem autenticação.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class PrivacyController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("privacidade")]
    [HttpGet("privacy")]
    [Produces("text/html")]
    public ContentResult Get()
    {
        var configuredEmail = configuration["Privacy:ContactEmail"]?.Trim();
        var configuredSupportUrl = configuration["Privacy:SupportUrl"]?.Trim();
        var supportUrl = Uri.TryCreate(configuredSupportUrl, UriKind.Absolute, out var supportUri) &&
                         supportUri.Scheme == Uri.UriSchemeHttps
            ? supportUri.ToString()
            : "https://github.com/DiogoJP202/MedicMark/issues";
        var support = $"<a href=\"{WebUtility.HtmlEncode(supportUrl)}\">suporte do MedicMark no GitHub</a>";
        var contact = string.IsNullOrWhiteSpace(configuredEmail)
            ? support
            : $"{support} ou <a href=\"mailto:{WebUtility.HtmlEncode(configuredEmail)}\">{WebUtility.HtmlEncode(configuredEmail)}</a>";

        return Content(
            $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Política de Privacidade — Checklist de Plantão</title>
              <style>
                :root { color-scheme: light dark; font-family: system-ui, sans-serif; line-height: 1.6; }
                body { margin: 0; }
                main { max-width: 760px; margin: 0 auto; padding: 32px 20px 64px; }
                h1, h2 { line-height: 1.2; }
                h2 { margin-top: 2rem; }
                .meta { opacity: .75; }
              </style>
            </head>
            <body>
            <main>
              <h1>Política de Privacidade do Checklist de Plantão</h1>
              <p class="meta">Vigente a partir de 22 de agosto de 2026.</p>

              <p>O Checklist de Plantão é uma ferramenta operacional destinada às equipes das
              instituições que adotam o sistema. A instituição responsável pela instalação
              administra os usuários, os setores, a retenção e o acesso aos dados.</p>

              <h2>Dados tratados</h2>
              <ul>
                <li><strong>Conta e acesso:</strong> nome de usuário, nome de exibição, situação,
                grupos e setores permitidos. A senha é transmitida por conexão criptografada e
                guardada somente na forma de hash; nunca em texto legível.</li>
                <li><strong>Operação do plantão:</strong> setor, data da sessão, códigos de leitos,
                tarefas concluídas, classificações e horários de sincronização.</li>
                <li><strong>Dispositivo e diagnóstico:</strong> identificador gerado pelo app,
                nome do aparelho, plataforma, versão, último contato e estado de sincronização e
                notificações.</li>
                <li><strong>Segurança:</strong> o endereço IP e dados técnicos da requisição podem
                ser processados nos registros do servidor e na limitação de tentativas de acesso.</li>
              </ul>

              <p><strong>O sistema não cadastra dados de pacientes.</strong> Não há nome, CPF,
              prontuário, diagnóstico ou informação clínica de paciente. Também não registra qual
              usuário marcou cada tarefa.</p>

              <h2>Finalidades</h2>
              <p>Os dados são usados somente para autenticar o acesso, aplicar permissões, exibir e
              sincronizar o checklist, permitir o uso offline, enviar notificações locais e manter
              a segurança e a disponibilidade do serviço.</p>

              <h2>Armazenamento local e notificações</h2>
              <p>O aplicativo mantém no aparelho uma cópia operacional para funcionar sem internet.
              Tokens ficam no armazenamento seguro do sistema e a senha não é armazenada. As
              notificações são agendadas localmente no aparelho e não usam publicidade nem
              rastreamento.</p>

              <h2>Compartilhamento</h2>
              <p>Não vendemos dados e não usamos redes de anúncios, analytics de terceiros ou
              rastreamento entre aplicativos. O provedor de infraestrutura pode processar os dados
              apenas para hospedar e proteger o serviço, sob as instruções do responsável pela
              instalação.</p>

              <h2>Retenção</h2>
              <p>Os prazos são administrados pela instituição. Na configuração padrão, tokens de
              renovação expiram em 30 dias, registros de sincronização são mantidos por 30 dias,
              operações idempotentes por 7 dias e dispositivos sem contato são marcados inativos
              após 30 dias. Sessões encerradas têm retenção operacional padrão de 24 horas. Cópias
              de segurança seguem a política da instituição.</p>

              <h2>Segurança</h2>
              <p>O serviço usa HTTPS, controle de acesso por setor e permissão, senhas com hash,
              tokens rotativos, bloqueio de tentativas e armazenamento seguro no aparelho. Nenhum
              sistema é infalível, mas adotamos medidas compatíveis com a natureza dos dados
              tratados.</p>

              <h2>Direitos e exclusão</h2>
              <p>Para consultar, corrigir ou solicitar a exclusão de dados de conta ou dispositivo,
              procure o administrador da instituição que forneceu seu acesso. Para problemas
              técnicos, use o {{contact}}. Desinstalar o aplicativo ou apagar seus dados remove a
              cópia local; dados do servidor devem ser tratados pelo administrador.</p>

              <h2>Público</h2>
              <p>O aplicativo não é direcionado a crianças. Ele é uma ferramenta de trabalho para
              profissionais autorizados pela instituição.</p>

              <h2>Alterações</h2>
              <p>Esta política pode ser atualizada quando o funcionamento do serviço ou requisitos
              legais mudarem. A data de vigência acima identifica a versão atual.</p>

              <h2>Contato</h2>
              <p>Suporte técnico: {{contact}}. Vulnerabilidades devem ser relatadas pelo canal
              privado de segurança do repositório, nunca em uma issue pública.</p>
            </main>
            </body>
            </html>
            """,
            "text/html; charset=utf-8");
    }
}
