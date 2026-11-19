# OBSERVAÇÃO

Dado que atualmente a aplicação está configurada para rodar especificamente na minha máquina, dados sensiveis, como meu endereço de ip, estão nos scripts. Antes de disponibilizar nesse repositório público, irei apagar partes do código e passar orientações em como configurar ao utilizar no computador.

# Sistema de Colaboração Remota Baseado em XR e Reconstrução Espacial

Este repositório contém o código-fonte principal (scripts) de um sistema de colaboração remota que utiliza Realidade Estendida (XR) e técnicas avançadas de reconstrução espacial em tempo real.

O objetivo do projeto é permitir que um especialista (Usuário Remoto) navegue livremente por uma reconstrução 3D do ambiente físico de um operador (Usuário Local), podendo enviar instruções espaciais que são projetadas diretamente no campo de visão do operador via Meta Quest.

## 🏗️ Arquitetura do Sistema

O projeto foi arquitetado em três frentes principais:

```mermaid
graph LR
    Local[Usuário Local<br>Meta Quest / XR] -- 1. Streaming de Vídeo --> Servidor((Servidor<br>Processamento de IA))
    Servidor -- 2. Reconstrução 3D<br>(Nuvem de Pontos/Splats) --> Remoto[Usuário Remoto<br>Navegação 3D Livre]
    Remoto -- 3. Instruções Espaciais --> Servidor
    Servidor -- 4. Projeção XR --> Local
