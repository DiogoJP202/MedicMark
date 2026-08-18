# Apresentação — ChecklistPlantão

Réplica navegável do aplicativo, em HTML/CSS/JS puro, para demonstrar sem depender de servidor,
banco, celular ou rede.

## Como abrir

Duas formas — a primeira é a mais simples:

1. **Clique duplo em `index.html`.** Abre no navegador e funciona.
2. Servidor local, se preferir uma URL:

```bash
dotnet run apresentacao/servir.cs
```

Depois acesse `http://localhost:5173`.

## O que é fiel ao produto

- `css/design-system.css` é **cópia literal** do arquivo do aplicativo — mesmas cores, mesmos
  alvos de toque, mesma tipografia.
- A estrutura de marcação das telas repete a dos componentes Razor.
- Os dados vêm do `SeedCatalog` real: setor Oeste, 16 leitos (1148–1169), Gelo (20H, 22H, 00H,
  02H, 04H, 06H), Glicemia (Jantar 19:30, Café 07:00), SSVV (PM 20:00, AM 06:00) e os marcadores
  C.I., Sondas e Drenos.
- Responsivo igual ao aplicativo: matriz no computador, uma coluna por vez no celular (a mudança
  acontece em 900px — estreite a janela para ver).

## O que funciona de verdade na demonstração

- marcar e desmarcar, com o `X` aparecendo na hora e **Desfazer** por alguns segundos;
- bloqueio de 1 segundo **só na célula tocada**, para o toque repetido não alternar sem querer;
- busca por leito, "somente pendentes" e filtro por classificação, combináveis;
- classificações por leito, atualizando sem perder a rolagem;
- pendências agrupadas por horário, com as atrasadas primeiro;
- contador de sincronização subindo e descendo a cada marcação;
- botão **"Simular problema"** na tela Dispositivo, que faz aparecer a faixa vermelha de alerta.

## O que é apenas visual

Encerrar e reiniciar o plantão estão desativados — são ações destrutivas e, no produto, exigem
servidor. A tela de administração é somente leitura.

O login foi pulado de propósito: a demonstração já começa autenticada no setor Oeste.

## Limites herdados do produto

Não há **nenhum dado de paciente** — nome, prontuário ou diagnóstico não existem no sistema real
e também não existem aqui. Não há registro de quem marcou cada tarefa.

## Arquivos

```
index.html              casca do aplicativo (topo, navegação, área de conteúdo)
css/design-system.css   cópia literal do design system do produto
css/apresentacao.css    o pouco que é exclusivo da demonstração
js/dados.js             dados fictícios e as regras de contagem
js/app.js               telas e interações
servir.cs               servidor estático opcional
```
