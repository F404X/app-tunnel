# AppTunnel 0.8.0

O AppTunnel conecta o Serein e outros clientes Discord à Proton VPN por meio de perfis WireGuard. A interface abre em inglês. Em **Language**, escolha **English**, **Português** ou **Русский**.

## Instalar e conectar

1. Baixe o **AppTunnel-Setup-x64.exe** em [Releases](https://github.com/F404X/app-tunnel/releases). Execute o instalador e aceite as opções do WireSock CLI e do Microsoft Edge WebView2 se elas aparecerem. Veja o [guia completo](README.md) para usar o ZIP portátil.
2. Abra **AppTunnel.exe** e confirme a elevação. Em **Rotas → Perfis da conta**, entre na Proton e consulte os perfis encontrados. O AppTunnel salva automaticamente os perfis compatíveis com chave privada disponível na janela.
3. Para criar um perfil, clique em **Novo WireGuard…**, dê um nome e clique em **Criar com servidor recomendado**. Para escolher a localização, use **Escolher outro servidor…** e clique em **Criar** ao lado do servidor na lista. Mantenha a janela aberta até que o perfil apareça em **Rotas**. [Guia da Proton](https://protonvpn.com/support/wireguard-configurations)
4. Em **Programas**, use **Explorar aplicativos comuns** para selecionar Serein, Discord e outros clientes detectados. Marque os programas que devem usar a VPN e clique em **Conectar**. Se Discord, PTB ou Canary estiver aberto e incluído na rota, confirme se deseja reiniciá-lo.
5. Em **Rotas → Protocolo VPN**, use **Automático** para tentar alternativas entre os perfis importados. Veja os [requisitos e limites](README.md#redes-com-filtragem-e-dpi). Para mudar de rota, selecione outro perfil e clique em **Trocar servidor**. O cartão e a coluna **Sessão** identificam o servidor em uso.

Para usar um perfil antigo cuja chave já não aparece na Proton, importe o **.conf original** ou crie um novo. Os perfis já salvos pelo aplicativo são migrados. Entre na Proton pelo AppTunnel para vincular a janela desta versão. A janela integrada usa o WebView2 sem elevação. [Código da Proton](https://github.com/ProtonMail/WebClients/blob/main/packages/components/containers/vpn/WireGuardConfigurationSection/WireGuardConfigurationSection.tsx)

## Bandeja e serviço

- Fechar a janela mantém o aplicativo na bandeja e o túnel ativo como serviço do Windows.
- **Ocultar ícone** esconde o ícone. Abra o executável novamente para restaurar a interface e a bandeja.
- **Sair da interface** encerra o aplicativo da bandeja e mantém o serviço ativo. Ao reabrir, o AppTunnel mostra o servidor em uso.
- Na versão instalada, **Iniciar com o Windows** abre o aplicativo na bandeja ao entrar no Windows. A opção fica em **Configurações** e no menu da bandeja.
- Use **Configurações → Verificar atualizações** para baixar e verificar a versão mais recente e confirmar quando quiser instalar.
- **Desconectar VPN** encerra e remove o serviço criado para a sessão. Depois de reiniciar o Windows, conecte novamente.

Os perfis são protegidos pela conta do Windows. Durante a conexão, o serviço lê a configuração da sessão de uma pasta com acesso restrito em ProgramData. O AppTunnel remove essa cópia ao desconectar. Mantenha seus arquivos `.conf` privados.

Leia os [Termos de uso](docs/terms/pt.txt) ou abra **Configurações → Termos de uso** no AppTunnel. O código do projeto está sob a [licença MIT](LICENSE).
