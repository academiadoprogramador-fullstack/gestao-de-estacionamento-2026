# Spec Delta — Check-in com pré-seleção de fotos

## ADDED Requirements

### Requirement: Seleção prévia de fotos no formulário de check-in

O check-in SHALL permitir selecionar até cinco arquivos JPEG, PNG ou WebP estáticos de até 5 MiB cada antes do envio, mantendo-os somente em memória e sem incluí-los no corpo, na `Idempotency-Key` ou na transação da entrada. O check-in SHALL continuar válido sem fotos, e a validação local SHALL ser apenas feedback; o servidor revalidará o conteúdo.

#### Scenario: Fotos selecionadas antes do check-in

- **WHEN** Caixa ou Administrador seleciona fotos válidas e preenche os campos obrigatórios
- **THEN** a tela mostra as fotos como “selecionadas, aguardando check-in”, não inicia upload e envia a entrada sem conteúdo de foto

#### Scenario: Limite ou arquivo inválido na seleção

- **WHEN** o usuário tenta selecionar uma sexta foto, formato não aceito ou arquivo acima de 5 MiB
- **THEN** a seleção é recusada localmente com mensagem associada ao campo e o check-in pode seguir sem essa foto

#### Scenario: Check-in rejeitado ou incerto antes das fotos

- **WHEN** o check-in é rejeitado por validação/regra ou sua resposta fica incerta
- **THEN** nenhuma foto é enviada; as seleções permanecem somente em memória para correção ou repetição explícita da mesma tentativa

### Requirement: Envio de fotos após a confirmação do check-in

Após o check-in confirmado, a SPA SHALL carregar a versão corrente do atendimento e enviar cada foto selecionada individualmente, com `Idempotency-Key` própria, sem alterar a entrada já confirmada. Falha de uma foto SHALL NOT desfazer o check-in, SHALL permitir repetição explícita pela mesma chave/conteúdo e SHALL manter as fotos confirmadas.

#### Scenario: Check-in confirmado e fila de fotos

- **WHEN** o check-in retorna `201` e a versão do atendimento é carregada
- **THEN** a tela exibe a confirmação da entrada com o identificador e inicia o envio sequencial das fotos selecionadas, mostrando estado por foto

#### Scenario: Falha isolada de foto

- **WHEN** uma foto falha no processamento ou armazenamento depois do check-in confirmado
- **THEN** a entrada permanece registrada e a tela oferece repetir apenas a foto afetada, sem novo check-in e sem nova chave automática

#### Scenario: Versão ou detalhe indisponível

- **WHEN** a consulta da versão do atendimento falha após o check-in
- **THEN** a entrada permanece confirmada, nenhuma foto é enviada e a tela oferece recarregar o atendimento ou continuar sem fotos

#### Scenario: Saída antes da conclusão das fotos

- **WHEN** o usuário troca de ator/rota, faz logout, inicia outro check-in ou confirma a saída do navegador
- **THEN** as seleções ainda não enviadas são descartadas, sem storage, cookie ou restauração; as fotos já confirmadas permanecem no atendimento
