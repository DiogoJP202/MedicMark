# Implantação

O `Dockerfile`, o Compose, a persistência, o health check e a restauração após reinicialização
foram validados em 22/08/2026, em Ubuntu 24.04 Minimal x86_64. O ensaio usou uma VM Oracle Cloud
`VM.Standard.E2.1.Micro` com 1 GB de RAM e 2 GB de swap.

## Segredos

Nenhum segredo vive no repositório.

Gerar a chave de assinatura:

```bash
openssl rand -base64 48
```

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

| Variável | Para quê |
|---|---|
| `Jwt__SigningKey` | Assinatura do JWT. Mínimo 32 caracteres. **Obrigatória.** |
| `Bootstrap__AdminUserName` | Administrador inicial. Só usada quando não existe nenhum usuário |
| `Bootstrap__AdminPassword` | Senha do administrador inicial |
| `Database__Path` | Arquivo SQLite. **Precisa estar em volume persistente** |

Em desenvolvimento, use User Secrets em vez de variáveis de ambiente:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<chave>" --project src/ChecklistPlantao.Server
```

## Docker

```bash
cp deploy/.env.example deploy/.env
```

Preencha `deploy/.env` e suba:

```bash
mkdir -p deploy/data-protection
sudo chown 10001:10001 deploy/data-protection
chmod 700 deploy/data-protection
docker compose -f deploy/docker-compose.yml up -d --build
```

```bash
docker compose -f deploy/docker-compose.yml logs -f servidor
```

O banco fica no volume nomeado `checklistplantao-dados`, montado em `/data`. As chaves de Data
Protection ficam em `deploy/data-protection`. O contêiner roda com usuário sem privilégios
(uid 10001), por isso esse diretório precisa pertencer a ele.

Depois que o administrador for criado, **remova** `ADMIN_USERNAME` e `ADMIN_PASSWORD` do `.env` e
recrie o contêiner: eles só têm efeito enquanto não há usuários.

## Windows como serviço

Publicar:

```bash
dotnet publish src/ChecklistPlantao.Server -c Release -o C:\ChecklistPlantao\servidor
```

Registrar o serviço (PowerShell como administrador):

```powershell
New-Service -Name ChecklistPlantao -BinaryPathName 'C:\ChecklistPlantao\servidor\ChecklistPlantao.Server.exe' -DisplayName 'Checklist de Plantão' -StartupType Automatic
```

As variáveis de ambiente do serviço ficam em
`HKLM\SYSTEM\CurrentControlSet\Services\ChecklistPlantao\Environment` (tipo `REG_MULTI_SZ`), uma
por linha:

```
ASPNETCORE_URLS=http://+:5000
Jwt__SigningKey=<chave>
Database__Path=C:\ChecklistPlantao\dados\checklistplantao.db
```

Liberar a porta no firewall — restrinja à sub-rede da instituição:

```powershell
New-NetFirewallRule -DisplayName 'Checklist de Plantão' -Direction Inbound -Protocol TCP -LocalPort 5000 -RemoteAddress 192.168.0.0/24 -Action Allow
```

Iniciar:

```powershell
Start-Service ChecklistPlantao
```

## Acesso pela rede local

O servidor precisa escutar em todas as interfaces, não em `localhost`:

```
ASPNETCORE_URLS=http://+:5000
```

Descobrir o endereço a informar nos aparelhos:

```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' } | Select-Object IPAddress, InterfaceAlias
```

Nos aparelhos, use esse IP na tela de configuração inicial — por exemplo
`http://192.168.0.10:5000`. Fixe o IP do servidor por reserva de DHCP: se ele mudar, todos os
aparelhos param de sincronizar até serem reconfigurados.

Conferir de outra máquina:

```bash
curl http://192.168.0.10:5000/api/server-info
```

## HTTPS em produção

Em produção o servidor exige HTTPS (`UseHsts` + `UseHttpsRedirection`). **Nunca desabilite a
validação de certificado no aplicativo** — isso anularia a proteção do tráfego na rede.

Duas opções:

**Reverse proxy** (recomendado). Nginx, Caddy ou IIS termina o TLS e encaminha para o servidor em
HTTP interno. Um certificado de uma autoridade pública, se houver nome de domínio resolvível.

**Certificado interno.** Se a instituição usa uma autoridade certificadora própria, o certificado
raiz precisa ser instalado nos aparelhos:

- **Android:** Configurações → Segurança → Criptografia e credenciais → Instalar certificado →
  Certificado CA. A partir do Android 7, um certificado instalado pelo usuário **não** é aceito
  pelo aplicativo sem configuração de `network_security_config` — planeje isso.
- **Windows:** importar no repositório "Autoridades de Certificação Raiz Confiáveis" da máquina.

Em desenvolvimento e na rede interna, HTTP é aceitável e está documentado — mas não em produção.

## Backup e restauração

Backup (funciona com o servidor em execução quando o `sqlite3` está disponível):

```powershell
./deploy/scripts/backup.ps1 -DatabasePath C:\ChecklistPlantao\dados\checklistplantao.db -DestinationDirectory C:\Backups
```

Restauração (**com o servidor parado**):

```powershell
./deploy/scripts/restore.ps1 -BackupPath C:\Backups\checklistplantao-20260806-190000.db -DatabasePath C:\ChecklistPlantao\dados\checklistplantao.db
```

Em Docker, o volume pode ser copiado com o contêiner parado:

```bash
docker run --rm -v checklistplantao-dados:/data -v "$PWD":/backup alpine tar czf /backup/dados.tar.gz -C /data .
```

Em um host Linux, o backup online validado usa `sqlite3`, o script Bash e um timer do systemd:

```bash
sudo apt-get install -y sqlite3
sudo install -m 0750 deploy/scripts/backup.sh /usr/local/sbin/medicmark-backup
sudo install -m 0644 deploy/systemd/medicmark-backup.service /etc/systemd/system/
sudo install -m 0644 deploy/systemd/medicmark-backup.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now medicmark-backup.timer
sudo systemctl start medicmark-backup.service
```

Por padrão ele grava em `/opt/medicmark/backups`, executa às 06:00 UTC, valida cada cópia com
`PRAGMA integrity_check` e remove arquivos com mais de 30 dias. `DATABASE_PATH`, `BACKUP_DIR` e
`RETENTION_DAYS` podem sobrescrever esses padrões.

O que o backup preserva de fato: usuários, grupos, setores, leitos, tipos de checklist, colunas,
marcadores e configurações. As marcações do plantão são temporárias por decisão do cliente e podem
já ter sido apagadas pela retenção.

## Monitoramento

| Endpoint | Uso |
|---|---|
| `/health` | Verificação geral |
| `/health/live` | O processo está vivo (não consulta o banco) |
| `/health/ready` | Banco acessível e sem migration pendente |

Logs estruturados em stdout, com categorias por assunto. Em Docker:

```bash
docker compose -f deploy/docker-compose.yml logs --tail 200 servidor
```

## Atualização

### Pré-implantação da sessão aberta única

A migração `SessaoAbertaUnicaPorSetor` troca o índice antigo, que incluía a data, por
`IX_Sessoes_Setor_Aberta`, único em `SectorId` enquanto `Status = 'Open'`. Antes de publicar uma
versão que a contenha, execute no SQLite central:

```sql
SELECT SectorId, COUNT(*)
FROM Sessoes
WHERE Status = 'Open'
GROUP BY SectorId
HAVING COUNT(*) > 1;
```

Se a consulta devolver qualquer linha, **aborte a implantação** e reconcilie as sessões
manualmente com a equipe responsável. A migração não escolhe nem encerra uma sessão
automaticamente. Depois de a consulta voltar vazia, faça o backup do arquivo central antes de
subir a nova versão.

### Sequência

1. Executar a consulta de duplicidades acima quando a versão incluir essa migração.
2. Fazer backup do SQLite central.
3. Publicar a nova versão.
4. As migrations são aplicadas automaticamente na subida.
5. Conferir `/health/ready` e os logs da aplicação da migração.
6. Os aparelhos recebem a configuração nova na próxima sincronização e reagendam as notificações.
