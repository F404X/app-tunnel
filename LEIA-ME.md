# AppTunnel 0.4.0

O AppTunnel conecta o Serein e outros clientes Discord à Proton VPN por meio de perfis WireGuard. A interface abre em inglês. Escolha **Language → Português** para mudar o idioma.

## Instalar e conectar

1. Extraia o ZIP inteiro. Instale o **.NET Desktop Runtime 10 x64**, o **WireSock Secure Connect CLI 3.8.2.1 ou superior** e o **Microsoft Edge WebView2 Runtime**. Os comandos estão no [README](README.md).
2. Abra **AppTunnel.exe** e confirme a elevação. Em **Rotas → Conta Proton**, entre na página oficial. **Perfis da conta** lista os perfis encontrados. O AppTunnel salva automaticamente os compatíveis com chave privada disponível na janela.
3. Clique em **Novo WireGuard** para abrir o formulário oficial, informar um nome, escolher um servidor e criar o perfil. O AppTunnel salva o perfil assim que a Proton o exibe. [Guia da Proton](https://protonvpn.com/support/wireguard-configurations)
4. Em **Programas**, selecione o Serein ou os executáveis que devem usar a VPN. Clique em **Conectar** e reabra o cliente de chat.
5. Para mudar de rota, selecione outro perfil e clique em **Trocar servidor**. O cartão e a coluna **Sessão** identificam o servidor em uso.

Para usar um perfil antigo cuja chave já não aparece na Proton, importe o **.conf original** ou crie um novo. Os perfis já salvos pelo aplicativo são migrados. Entre na Proton pelo AppTunnel para vincular a janela desta versão. A janela integrada usa o WebView2 sem elevação. [Código da Proton](https://github.com/ProtonMail/WebClients/blob/main/packages/components/containers/vpn/WireGuardConfigurationSection/WireGuardConfigurationSection.tsx)

## Bandeja e serviço

- Fechar a janela mantém o aplicativo na bandeja e o túnel ativo como serviço do Windows.
- **Ocultar ícone** esconde o ícone. Abra o executável novamente para restaurar a interface e a bandeja.
- **Sair da interface** encerra o aplicativo da bandeja e mantém o serviço ativo. Ao reabrir, o AppTunnel mostra o servidor em uso.
- **Desconectar VPN** encerra e remove o serviço criado para a sessão. Depois de reiniciar o Windows, conecte novamente.

Os perfis são protegidos pela conta do Windows. Durante a conexão, o serviço lê a configuração da sessão de uma pasta com acesso restrito em ProgramData. O AppTunnel remove essa cópia ao desconectar. Mantenha seus arquivos `.conf` privados.
