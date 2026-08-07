# Segurança

## Autenticação online

- ASP.NET Identity Core com hash PBKDF2 do próprio framework.
- JWT de curta duração (30 min por padrão), assinado com HMAC-SHA256.
- Refresh token de 64 bytes aleatórios, guardado **apenas como hash SHA-256**: vazar a tabela não
  permite reutilizar tokens.
- Rotação a cada uso. Reapresentar um refresh já consumido **revoga toda a cadeia** daquele
  usuário — é o sinal clássico de token roubado.
- Bloqueio após tentativas inválidas (5 tentativas / 15 min, configurável).
- Limite de requisições em `/api/auth/login` e `/api/auth/refresh`, particionado por IP.
- HTTPS obrigatório fora de desenvolvimento (`UseHsts` + `UseHttpsRedirection`).

A resposta a credenciais inválidas é **idêntica** para usuário inexistente e senha errada. Não
damos ao atacante um oráculo de nomes de usuário — há um teste automatizado verificando isso.

## Autenticação offline

O primeiro acesso de um aparelho **exige servidor**. Depois disso vale por 7 dias configuráveis
desde a última validação online.

O que garante a segurança do acesso offline:

- a senha **nunca** é armazenada, nem em texto puro nem de forma reversível;
- o `PasswordHash` do Identity **nunca sai do servidor** — não existe endpoint que o exponha, e
  `IUserCredentialStore` não tem método para lê-lo;
- o verificador local é derivado **no próprio aparelho**, da senha que o usuário digitou, com
  PBKDF2-SHA256, 210 000 iterações e sal aleatório de 16 bytes por dispositivo;
- a comparação é em tempo fixo (`CryptographicOperations.FixedTimeEquals`);
- limite de tentativas locais com bloqueio temporário;
- tokens no armazenamento seguro da plataforma — Keystore no Android, DPAPI no Windows — nunca no
  banco local.

**Risco aceito e declarado:** quem obtém o banco do aparelho pode tentar força bruta offline. É
por isso que existem iterações altas, limite de tentativas e validade curta. Reduzir
`acessoOffline.validadeDias` diminui a janela.

## Autorização

- Permissões por **chave**, não por papel. Não existe "é admin" implícito no código.
- O acesso efetivo é a **união** dos grupos ativos do usuário: permissões e setores somam.
- Políticas geradas sob demanda a partir de `perm:<chave>` — criar uma permissão nova não exige
  lembrar de registrar a política.
- **Todo endpoint protegido valida no servidor.** Esconder botão é UX; a recusa acontece no
  servidor. Há testes de integração atacando os endpoints diretamente, sem passar por tela.
- Acesso a setor é verificado em toda operação de sessão, checklist e marcador. Um identificador
  enviado pelo cliente nunca é aceito sem verificar o acesso.

Alterar um grupo revoga os tokens de todos os membros: a mudança precisa valer na hora, não quando
o token expirar.

## O que nunca é registrado em log

- senhas, em qualquer forma;
- tokens de acesso ou de atualização;
- verificadores offline, sal ou hash;
- conteúdo completo de requisição de autenticação;
- dados pessoais — não existem no sistema.

Os logs registram identificadores, contagens e resultados. Ao redefinir uma senha, registra-se o
fato, não o valor.

## Dados que o sistema não guarda

Por decisão do cliente, e verificado por teste automatizado:

- nenhum dado de paciente: nome, CPF, prontuário, diagnóstico, informação clínica;
- nenhuma autoria de marcação — `ChecklistEntry` e `SessionBedMarker` não têm campo de usuário.

## Proteções de implementação

- DTOs separados das entidades em toda a API: não há mass assignment.
- Controle de concorrência por versão em toda entidade sincronizável.
- Índices únicos no banco impedem duplicidade mesmo com requisições simultâneas.
- `ProblemDetails` padronizado; **stack trace nunca** vai ao cliente em produção.
- CORS desligado por padrão; quando habilitado, exige lista explícita de origens — nunca `*`.
- Segredos fora do repositório: variáveis de ambiente ou User Secrets. Há teste garantindo que a
  sonda anônima do servidor não expõe caminho de banco nem chave.

## Antes de ir para produção

1. Gerar uma chave `Jwt:SigningKey` aleatória e exclusiva, com no mínimo 32 caracteres.
2. Configurar HTTPS com certificado confiável nos aparelhos — ver [DEPLOYMENT.md](DEPLOYMENT.md).
3. **Nunca** desabilitar validação de certificado.
4. Trocar a senha do administrador inicial e remover `Bootstrap__AdminPassword` do ambiente.
5. Restringir a porta do servidor à rede da instituição.
6. Configurar backup — ver `deploy/scripts/backup.ps1`.
