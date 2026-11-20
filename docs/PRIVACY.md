# Privacidade e feedback do AppTunnel

Responsável pelo projeto **F404X**. As informações desta página e atualizações do aplicativo estão no [repositório oficial do AppTunnel](https://github.com/F404X/app-tunnel).

## Suas escolhas

As três opções são independentes e começam desmarcadas. Você pode autorizá-las no instalador ou em **Configurações → Privacidade e feedback**. As opções são guardadas por conta do Windows. Uma atualização reaproveita as escolhas. Uma instalação silenciosa deixa as preferências existentes intactas.

| Opção | Dados do corpo JSON | Quando ocorre o envio |
| --- | --- | --- |
| **Enviar estatísticas anônimas** | Versão do AppTunnel, família do Windows, contagem de aberturas e contagens por protocolo. O tempo para aplicar uma rota entra nas faixas abaixo de 5 s, de 5 s a menos de 20 s e 20 s ou mais. As tentativas também incluem uma contagem de falhas. | Quando há dados acumulados, com intervalo mínimo de 24 horas entre tentativas de envio. |
| **Relatórios automáticos de erros** | Versão, família do Windows, etapa e código de falha. Cada exceção é reduzida a um código técnico predefinido. | Ao ocorrer uma falha. A mesma combinação de etapa e código gera no máximo uma tentativa em 24 horas. |
| **Incluir diagnósticos detalhados** | Versão, família do Windows e até 24 eventos técnicos recentes, em ordem. Os eventos representam o estado da sessão, do serviço e do handshake. | O usuário abre a prévia e confirma cada relatório individualmente. |

O campo `schema` indica a versão do formato. O campo `kind` indica o tipo do relatório. O sistema agrupa Windows 10, Windows 11 e outros. As contagens são limitadas a 1.000 por campo na estação do usuário.

**Visualizar dados enviados** mostra exemplos do esquema, os corpos disponíveis para prévia e o histórico local da entrega. A primeira prévia de estatísticas congela o corpo do próximo envio. Eventos posteriores entram em outro lote. A confirmação de diagnósticos autoriza exatamente o JSON exibido. O painel guarda localmente o corpo UTF-8 de cada tentativa e informa quando a API confirmou o recebimento.

IP, conta Proton, credenciais, chaves VPN, nome do usuário, caminhos de arquivos, destinos acessados, conteúdo do tráfego, mensagens originais de exceções e identificadores de instalação ficam fora do esquema. A Cloudflare recebe o endereço de origem para transportar a requisição HTTPS. O código do app e da API de telemetria não consulta esse endereço, e os registros de observabilidade do Worker ficam desligados. Os dados são enviados diretamente ao endpoint indicado no build, sem parâmetros de rastreamento ou cookies. O código atual mantém a transmissão indisponível em compilações sem endpoint.

## Revogação, exclusão e retenção

Desmarcar uma opção cancela envios em curso daquela categoria e interrompe novas coletas e envios opcionais. Uma requisição já aceita pelo servidor permanece nas contagens agregadas. Uma tentativa sem confirmação aparece no histórico como tal. A API usa o dia UTC de recebimento para agregar as contagens.

O histórico local retém até 60 tentativas por até 30 dias, com limpeza quando o AppTunnel roda. Use **Apagar dados locais** no visualizador para excluir o histórico e os dados acumulados. Os marcadores usados para evitar novas tentativas no mesmo período são conservados até expirar. A desinstalação remove o consentimento e o arquivo de telemetria da conta que a executa.

O servidor retém até 30 dias de agregados e elimina os registros expirados diariamente. O administrador pode excluir todas as contagens no painel protegido. Como os relatórios são agregados sem identificador individual, a API não consegue vincular um agregado a uma instalação para excluí-la isoladamente. A restauração interna do D1 disponível à conta segue a janela de recuperação do plano.

Para exercer direitos de privacidade ou reportar um problema, use o canal de contato indicado no [perfil oficial F404X](https://github.com/F404X). Envie apenas o necessário e use um canal privado para qualquer conteúdo que possa identificar você.

[Documentação do formato e infraestrutura](../telemetry/README.md) · [D1 Time Travel](https://developers.cloudflare.com/d1/reference/time-travel/)
