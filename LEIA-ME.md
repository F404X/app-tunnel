# AppTunnel 0.13.2

O AppTunnel conecta o Serein e outros clientes Discord à Proton VPN por meio de perfis WireGuard. A interface abre no idioma do Windows (português, inglês ou russo; inglês nos demais casos). Em **Idioma (Language)**, escolha **English**, **Português** ou **Русский**.

## Instalar e conectar

1. Baixe o **AppTunnel-Setup-x64.exe** em [Releases](https://github.com/F404X/app-tunnel/releases). Execute o instalador e aceite as opções do WireSock CLI e do Microsoft Edge WebView2 se elas aparecerem. Veja o [guia completo](README.md) para usar o ZIP portátil.
2. Abra **AppTunnel.exe** e confirme a elevação. Na primeira abertura, o assistente confere o WireSock e o WebView2, abre o login da Proton e pergunta quais apps usam a VPN. O AppTunnel salva automaticamente os perfis compatíveis com chave privada disponível na janela.
3. Para criar outro perfil, abra **Servidores → Escolher rota** e clique em um servidor da lista. O AppTunnel cria a configuração na sua conta Proton em segundo plano, sem abrir o navegador; só pede o login da Proton se a sessão tiver expirado. A janela da Proton continua disponível em **Mais opções**. [Guia da Proton](https://protonvpn.com/support/wireguard-configurations)
4. Em **Apps**, ligue a chave dos programas que devem usar a VPN. **Adicionar app** abre o catálogo de apps comuns, a lista de apps abertos e a busca por `.exe` ou pasta; também dá para arrastar o arquivo para a janela. Em **Início**, clique no botão redondo para conectar. Se Discord, PTB ou Canary estiver aberto e incluído na rota, confirme se deseja reiniciá-lo. Com **Lembrar minha escolha**, a pergunta não aparece de novo. Durante a conexão, mudanças em apps e filtros são aplicadas automaticamente.
5. Em **Servidores**, **Automático** usa o servidor mais rápido e troca sozinho se ele ficar lento ou falhar. Para fixar um servidor, escolha a linha; com a VPN ligada, clique em **Trocar servidor** em **Início**. Em **Avançado → Protocolo**, **Automático** tenta alternativas entre os perfis importados. Veja os [requisitos e limites](README.md#redes-com-filtragem-e-dpi).

Para usar um perfil antigo cuja chave já não aparece na Proton, importe o **.conf original** ou crie um novo. Os perfis já salvos pelo aplicativo são migrados. Entre na Proton pelo AppTunnel para vincular a janela desta versão. A janela integrada usa o WebView2 sem elevação. [Código da Proton](https://github.com/ProtonMail/WebClients/blob/main/packages/components/containers/vpn/WireGuardConfigurationSection/WireGuardConfigurationSection.tsx)

## Bandeja e serviço

- Fechar a janela mantém o aplicativo na bandeja e o túnel ativo como serviço do Windows.
- **Ocultar ícone** esconde o ícone. Abra o executável novamente para restaurar a interface e a bandeja.
- **Sair da interface** encerra o aplicativo da bandeja e mantém o serviço ativo. Ao reabrir, o AppTunnel mostra o servidor em uso.
- Na versão instalada, **Iniciar com o Windows** abre o aplicativo na bandeja ao entrar no Windows. A opção fica em **Configurações** e no menu da bandeja.
- Use **Configurações → Verificar atualizações** para baixar e verificar a versão mais recente e confirmar quando quiser instalar.
- Em **Configurações → Atualizações → Canal de atualização**, escolha **Estável** (padrão), **Beta** (prévias da próxima versão) ou **Nightly** (uma versão experimental a cada mudança). O AppTunnel nunca instala uma versão anterior: ao voltar para um canal mais lento, ele espera esse canal alcançar a versão instalada.
- **Desconectar VPN** encerra e remove o serviço criado para a sessão. Depois de reiniciar o Windows, conecte novamente.

Os perfis são protegidos pela conta do Windows. Durante a conexão, o serviço lê a configuração da sessão de uma pasta com acesso restrito em ProgramData. O AppTunnel remove essa cópia ao desconectar. Mantenha seus arquivos `.conf` privados.

Leia os [Termos de uso](docs/terms/pt.txt) ou abra **Configurações → Termos de uso** no AppTunnel. O código do projeto está sob a [licença MIT](LICENSE).
