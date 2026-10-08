# Status verificável

## M01 - Aplicação inicial de inventário
- Código WPF e serviço somente leitura publicados em branch de funcionalidade.
- CI: build e publicação portátil para Windows x64 configurados.
- Artefatos são gerados pelo GitHub Actions e disponibilizados no run em **Artifacts**, não como GitHub Release.
- Instalador Inno Setup 6 disponível como script; sua compilação depende de uma instalação do Inno Setup.
- Nenhuma rotina de exclusão, formatação, redimensionamento, conversão ou recuperação foi implementada.
- **Não usar como ferramenta de manutenção destrutiva**.

## Gates obrigatórios antes de liberar operações de escrita
1. Teste em Windows 10 e 11 em máquinas virtuais.
2. Identificação segura por dispositivo e número de série.
3. Testes automatizados de seleção do disco e recusas de operações perigosas.
4. Testes em discos virtuais descartáveis VHDX para todas as operações.
5. Testes manuais em hardware não crítico e restauração de backup.
6. Revisão de código e evidências de auditoria.

O resultado de build não comprova operação correta com hardware real.
