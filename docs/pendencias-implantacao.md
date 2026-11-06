# Pendências de implantação

Este registro separa as verificações locais/CI das decisões necessárias para homologação e produção. Os estados abaixo são **pendente**; dados sintéticos usados em testes não constituem decisão, aprovação nem evidência de prontidão para Azure.

| Tema | Responsável | Estado | Critério para resolver |
| --- | --- | --- | --- |
| Fuso horário da unidade | Administrador/operação (a designar) | Pendente | Confirmar o fuso da unidade e validar exibição e agrupamento de eventos. |
| Moeda e arredondamento | Negócio/financeiro (a designar) | Pendente | Confirmar moeda, precisão e regra de arredondamento antes de produção. |
| Volume, picos e metas de desempenho | Produto/operação (a designar) | Pendente | Medir volume e picos em operação/piloto e aprovar metas de desempenho. |
| Domínios e certificados | Infraestrutura/segurança (a designar) | Pendente | Definir domínios, certificados e configuração HTTPS para homologação e produção. |
| Produto/SKU e topologia edge/API | Infraestrutura/segurança (a designar) | Pendente | Selecionar serviços/SKUs e validar proxy, ingress restrito da API e autenticação entre serviços em homologação Azure. |
| Privacidade: acesso, retenção e descarte | Negócio/privacidade (a designar) | Pendente — bloqueio de produção | Aprovar acesso, minimização, retenção e descarte de documentos, vistorias e histórico. |
| Backup, recuperação e chaves | Operação/segurança (a designar) | Pendente — bloqueio de produção | Definir política e retenção de backup; proteger/rotacionar chaves e testar restore de banco e key ring. |
| RPO/RTO e disponibilidade | Operação (a designar) | Pendente — bloqueio de produção | Aprovar metas de recuperação/disponibilidade e demonstrar que o procedimento atende às metas. |
| Contingência de falha de rede/serviço | Operação (a designar) | Pendente — bloqueio de produção | Aprovar procedimento operacional e ensaiá-lo antes da entrada em produção. |
| Homologação Azure e validação de publicação | Infraestrutura/segurança e responsáveis de negócio (a designar) | Pendente | Provisionar e validar a topologia real e as jornadas em homologação antes de qualquer decisão de produção. |

## Limites do marco local

Builds, testes locais/CI e dados sintéticos demonstram somente o comportamento local especificado. Não aprovam parâmetros de implantação, não validam isolamento de ingress, serviços Azure, políticas de privacidade ou operação e não autorizam publicação. A homologação Azure, as aprovações responsáveis e os gates de produção definidos em `docs/tdd.md` e `docs/prd.md` permanecem obrigatórios.
