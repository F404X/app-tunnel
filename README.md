# AppTunnel

<img src="src/AppTunnel.Windows/Assets/logo.png" alt="Logo do AppTunnel" width="72" height="72">

Use a **Proton VPN só nos aplicativos que você escolher**, como Serein, Discord ou outro cliente compatível. O AppTunnel permite selecionar os programas e a rota WireGuard pelo painel. O WireSock mantém a conexão como serviço do Windows e coloca o tráfego TCP e UDP dos programas escolhidos no túnel.

**Para começar, baixe o ZIP em [Releases](https://github.com/F404X/app-tunnel/releases).** O guia abaixo explica desde a instalação até o primeiro teste.

## Antes de instalar

- **Windows 10 ou 11 de 64 bits, arquitetura x64.** Para conferir, abra **Configurações → Sistema → Sobre** e procure **Tipo de sistema**.
- **Uma conta Proton.** Você pode criar uma conta gratuita e escolher um dos servidores disponíveis no seu plano. [Criar conta Proton VPN](https://protonvpn.com/free-vpn)
- **Permissão de administrador no Windows.** O sistema pede essa permissão porque o AppTunnel usa um serviço de rede.

## 1. Instale as dependências

Abra o menu **Iniciar**, digite **PowerShell** e abra o aplicativo. Copie **um comando por vez**, pressione **Enter** e aguarde terminar antes de colar o seguinte. Se o instalador pedir confirmação, leia o prompt e confirme.

```powershell
winget install --id Microsoft.DotNet.DesktopRuntime.10 --exact --source winget
winget install --id NTKERNEL.WireSockVPNClientCLI --exact --source winget
winget install --id Microsoft.EdgeWebView2Runtime --exact --source winget
```

| Dependência | O que ela faz |
| --- | --- |
| .NET Desktop Runtime 10 | Permite abrir o AppTunnel. [Download do .NET](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) |
| WireSock Secure Connect CLI | Fornece o serviço que conecta a VPN e filtra o tráfego por aplicativo. O AppTunnel requer a versão 3.8.2.1 ou superior. [Documentação do WireSock CLI](https://wiresock.net/documentation/wiresock-secure-connect/command-line-interface.html) |
| Microsoft Edge WebView2 Runtime | Abre a página oficial da Proton dentro da janela de conta do AppTunnel. [Download do WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) |

Se aparecer **“winget não é reconhecido”**, instale ou atualize o **Instalador de Aplicativo** na Microsoft Store e abra o PowerShell novamente. Se um pacote já estiver instalado e atualizado, siga para o próximo comando. [Ajuda do WinGet](https://learn.microsoft.com/pt-br/windows/package-manager/winget/)

## 2. Baixe e abra o AppTunnel

1. Abra [Releases](https://github.com/F404X/app-tunnel/releases). Na seção **Assets** da versão que você quer usar, baixe **`AppTunnel-Windows-x64.zip`**.
2. No Explorador de Arquivos, clique com o botão direito no ZIP e escolha **Extrair tudo**. Mantenha os arquivos juntos na pasta extraída.
3. Abra a pasta **AppTunnel** e dê dois cliques em **`AppTunnel.exe`**. O Windows pode mostrar o pedido de permissão de administrador para o serviço.
4. A interface abre em inglês. Para mudar, clique em **Language · EN → Português** no menu lateral. O restante do guia usa os nomes em português e indica os principais nomes em inglês entre parênteses.

Se já usava uma versão anterior, encerre a interface antiga pelo menu do ícone na bandeja antes de abrir esta versão. O AppTunnel migra seus perfis locais e preferências na primeira abertura.

## 3. Escolha o servidor da Proton

Um **perfil WireGuard** guarda o endereço do servidor e a chave necessária para conectar. Você pode criar um perfil na página da Proton ou importar um arquivo que já tenha guardado.

### Criar um perfil pelo AppTunnel

1. No menu lateral, abra **Rotas (Routes) → Conta Proton (Proton account)**.
2. Faça login na página oficial que abre na janela da Proton e conclua a verificação em duas etapas se a sua conta pedir.
3. Volte ao painel do AppTunnel e clique em **Novo WireGuard (New WireGuard)**. O botão leva ao formulário de criação na Proton.
4. Dê um nome fácil de reconhecer, como `AppTunnel casa`. Confira a opção **Windows**, escolha um servidor disponível no seu plano e clique em **Criar (Create)** na página da Proton.
5. Aguarde o servidor aparecer na tabela **Rotas** do AppTunnel. O perfil é salvo automaticamente quando a página exibe a configuração completa. Clique na linha da rota para selecioná-la.

Em **Perfis da conta (Account profiles)**, o AppTunnel lista os perfis que encontrou depois do login. Os perfis com uma chave utilizável são importados automaticamente. Se aparecer **Requer o .conf original**, importe o arquivo que você salvou quando criou aquele perfil ou crie um novo. A Proton usa uma chave privada gerada na criação do perfil. [Guia oficial da Proton](https://protonvpn.com/support/wireguard-configurations) · [Implementação da página de configuração](https://github.com/ProtonMail/WebClients/blob/main/packages/components/containers/vpn/WireGuardConfigurationSection/WireGuardConfigurationSection.tsx#L387-L404)

### Usar um arquivo `.conf` que você já tem

1. Abra **Rotas → Importar .conf (Import .conf)**.
2. Escolha o arquivo original baixado da Proton e clique em **Abrir**.
3. Clique na linha da rota importada. Se preferir gerar o arquivo no seu navegador, acesse [Downloads da conta Proton](https://account.protonvpn.com/downloads#wireguard-configuration) e use **Configuração WireGuard**.

Guarde os arquivos `.conf` em um local privado. Eles contêm a chave privada do perfil.

## 4. Selecione os aplicativos e conecte

1. Abra **Programas (Apps)**. Para usar a VPN só no Serein, clique em **Só Serein (Serein only)**. Para encontrar o Discord e clientes derivados instalados, clique em **Detectar Discord (Detect Discord)**.
2. Confira as caixas da coluna **VPN**. Marque os programas que devem usar o túnel. Se o programa estiver fora da pasta padrão, use **Adicionar .exe (Add .exe)** ou **Em execução… (Running apps…)**. Ao usar **Adicionar pasta**, escolha a pasta do próprio aplicativo, pois a regra abrange os executáveis nela.
3. Volte a **Rotas** e clique no servidor desejado. No painel superior, clique em **Conectar (Connect)**.
4. Quando aparecer **Serviço em execução (Service running)**, encerre o cliente de chat por completo, inclusive pelo ícone dele na bandeja, e abra-o novamente. Isso recria as conexões de voz e vídeo. Faça uma chamada ou teste o compartilhamento de tela.

O painel superior mostra o **servidor em uso** e seu endereço. Para trocar, selecione outra linha em **Rotas** e clique em **Trocar servidor (Switch server)**. Se mudar os aplicativos ou os filtros durante a conexão, clique em **Aplicar ajustes (Apply settings)**.

## Bandeja e desconexão

| O que você quer fazer | Onde clicar |
| --- | --- |
| Recolher a janela e continuar conectado | Feche a janela pelo **X**. O AppTunnel fica na bandeja do Windows, perto do relógio. Procure na seta de ícones ocultos se necessário. |
| Esconder o ícone do AppTunnel | Clique com o botão direito nele e escolha **Ocultar ícone (Hide Tray)**. |
| Restaurar a janela e o ícone | Abra `AppTunnel.exe` novamente. A instância existente é trazida de volta. |
| Encerrar a interface e manter o serviço | Na bandeja, escolha **Sair da interface (Exit interface)**. |
| Parar a VPN | Clique em **Desconectar (Disconnect)** no painel ou **Desconectar VPN (Disconnect VPN)** na bandeja. |

O serviço inicia sob demanda. Depois de reiniciar o Windows, abra o AppTunnel e clique em **Conectar** novamente.

## Se algo der errado

| O que aparece | Como resolver |
| --- | --- |
| O executável pede o .NET | Instale o **.NET Desktop Runtime 10 x64** do passo 1 e abra o executável novamente. |
| A janela Proton fica indisponível | Instale ou atualize o **WebView2 Runtime**. Você também pode baixar o perfil pela página oficial da Proton e usar **Importar .conf**. |
| O WireSock aparece como ausente | Instale o pacote **WireSock Secure Connect CLI** do passo 1. Em **Diagnóstico (Diagnostics)**, use **Detectar novamente (Detect again)**. Se ainda precisar indicar o caminho manualmente, use **Localizar WireSock… (Locate WireSock…)** e procure `wiresock-client.exe`, normalmente em `C:\Program Files\WireSock Secure Connect\sdk\`. |
| A importação diz que `PrivateKey` está vazia ou inválida | Use o `.conf` original que contém a chave privada ou gere um novo perfil na Proton. |
| O servidor aparece em uso e a chamada continua falhando | Confira se marcou o executável certo em **Programas**, aplique as alterações, feche o cliente de chat por completo e reabra. Se necessário, teste outro servidor e confira os registros em **Diagnóstico**. |
| Outro serviço WireSock já está em uso | Desconecte a sessão no aplicativo que a iniciou e tente conectar pelo AppTunnel. |

Para enviar um problema, abra uma [issue](https://github.com/F404X/app-tunnel/issues/new) com a versão do AppTunnel, a versão do Windows e os passos para reproduzir. Copie a mensagem de erro ou os registros relevantes de **Diagnóstico**. Mantenha as chaves privadas e os arquivos `.conf` fora do relato.

## Compilar e testar

Para compilar no Windows, instale o **SDK .NET 10** e execute na raiz do repositório. O resultado fica em `dist/win-x64`.

```powershell
.\Compilar.ps1
dotnet run --project tests/AppTunnel.Checks -c Release
node tests/proton-portal.test.cjs
```

| Projeto | Responsabilidade |
| --- | --- |
| `src/AppTunnel.Windows` | Interface, seleção de aplicativos, bandeja e controle do serviço WireSock. |
| `src/AppTunnel.Core` | Validação de perfis WireGuard, opções de rota e dados da integração Proton. |
| `src/AppTunnel.Proton` | Janela de conta Proton e importação dos perfis disponíveis. |

O código usa a [licença MIT](LICENSE). As licenças das dependências e fontes incluídas acompanham seus arquivos em [Licenses](Licenses) e [Fonts](src/AppTunnel.Windows/Fonts). A paleta da interface é a [Meia-noite do Cartigma beta](https://github.com/F404X/cartigma/blob/feature/ranking/themes.css).
