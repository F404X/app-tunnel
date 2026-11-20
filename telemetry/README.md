# Telemetria do AppTunnel na Cloudflare

O cliente transmite três tipos de relatórios opt-in. O Worker aceita exclusivamente uma versão de esquema, chaves exatas, categorias enumeradas, números limitados e corpos de até 4 KiB. O D1 recebe contagens agregadas em UTC por dia, versão, sistema, protocolo e código técnico. Há uma rota separada para leitura e exclusão administrativa, disponível após validação RS256 de um JWT do Cloudflare Access.

O painel da Pages serve conteúdo e gráficos sem serviços externos de análise e consulta a API por um *service binding*. A função de middleware cobre todas as rotas, inclusive arquivos estáticos e o domínio `pages.dev`. Ela valida a assinatura, o emissor, o público e a validade do JWT em cada requisição. Um host sem o Cloudflare Access e sem um token válido apresenta 403. O fluxo de login administrativo exige um hostname de produção coberto pelo aplicativo do Access.

## Implantação autorizada

Execute após confirmar **Workers Free**, **Pages Free**, **D1 Free** e **Zero Trust Free** na conta apropriada. Use uma conta autorizada via `wrangler login`, autenticação vinculada à Cloudflare ou credenciais gerenciadas fora do repositório. O aplicativo Windows utiliza somente uma URL HTTPS pública para ingestão.

1. Crie o banco D1 com **wrangler d1 create app-tunnel-telemetry**. Substitua o identificador de exemplo em `worker/wrangler.toml` em uma configuração local de implantação. Em seguida, execute **wrangler d1 migrations apply DB --remote** na pasta `worker`.
2. Crie o projeto Pages **app-tunnel-dashboard**. Conecte um hostname administrativo ao projeto e inclua toda a raiz desse hostname em um aplicativo **Self-hosted** no Cloudflare Access. Configure uma política de acesso que permita apenas os administradores autorizados.
3. Copie o emissor HTTPS da equipe e o identificador **Application Audience (AUD)** dessa aplicação para `ACCESS_ISSUER` e `ACCESS_AUD` nas duas configurações. Defina `DASHBOARD_ORIGIN` no Worker como a origem HTTPS administrativa exata. Atribua o binding **TELEMETRY** ao Worker de mesmo nome na Pages. Variáveis de produção e de prévia exigem a configuração correspondente ou continuam fechadas.
4. Implante o Worker com **wrangler deploy** em `worker` e o painel com **wrangler pages deploy public --project-name app-tunnel-dashboard** em `pages`.
5. Verifique, em produção, que páginas, arquivos estáticos e APIs administrativas exigem o Access. Acesse com uma conta permitida e confirme a leitura de agregados reais. Teste uma rejeição de JWT inválido e a instalação e a remoção com as três opções desmarcadas.
6. Após validar a implantação, configure a variável **APPTUNNEL_TELEMETRY_ENDPOINT** do repositório do GitHub com a URL HTTPS do Worker terminada em `/v1/reports`. Para builds locais, use a propriedade MSBuild `AppTunnelTelemetryEndpoint` ou a variável de ambiente correspondente. O aplicativo usa a URL incorporada ao build. GitHub, Cloudflare e as rotas administrativas têm credenciais e políticas próprias.

## Cotas, exclusão e indisponibilidade

- O Worker limita a entrada por localidade da rede Cloudflare, com uma chave comum para a API pública. Assim, o código não usa IPs nem IDs para limitar requisições.
- Um orçamento SQL diário limita o D1 a 5.000 operações sobre linhas de agregados e rejeita o relatório inteiro quando o orçamento acaba. Cada operação sobre agregados também atualiza o contador de orçamento. A limpeza diária remove agregados antigos e orçamentos expirados.
- A rota de agregados aceita períodos de 7, 14 ou 30 dias, filtros enumerados e consulta com parâmetros preparados. O painel usa recarga manual. A exclusão de agregados exige um JWT válido, origem administrativa e confirmação na interface.
- Ao atingir os limites dos planos gratuitos, a ingestão pode ficar indisponível. A falha de envio não bloqueia o túnel, e o cliente respeita o intervalo e a deduplicação locais. O projeto não exige nenhum recurso de plano pago. Ferramentas de observabilidade externas não devem ser ativadas para este endpoint.

[Limites de Workers](https://developers.cloudflare.com/workers/platform/limits/) · [Preços e cotas D1](https://developers.cloudflare.com/d1/platform/pricing/) · [Validação de JWT do Access](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/)

## Testes locais

Use **node --test tests/telemetry.test.mjs** na raiz do repositório com Node.js 24. Os testes usam a migração SQL em SQLite, verificam atomicidade, retenção, controles de acesso e rejeição de entradas inesperadas. Para testar os bindings no runtime da Cloudflare local, aplique a migração com **wrangler d1 migrations apply DB --local** e inicie **wrangler dev --local** na pasta do Worker. O pacote é configurável sem nenhuma conta para desenvolvimento local. As políticas, os limites e os dados de produção devem ser verificados após o deploy real.
